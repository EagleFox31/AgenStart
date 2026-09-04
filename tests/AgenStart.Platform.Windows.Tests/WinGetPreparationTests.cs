using System.Security.Cryptography;
using System.Text;
using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.WinGet;

namespace AgenStart.Platform.Windows.Tests;

public sealed class WinGetPreparationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AgenStartTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildDownload_UsesExactTrustedPackageWithoutSecurityBypasses()
    {
        var request = Request("git", "Git.Git");
        var output = Path.Combine(_root, "download");

        var command = WinGetCommandBuilder.BuildDownload(request, output);

        Assert.Equal("download", command.Arguments[0]);
        Assert.Contains("--id", command.Arguments);
        Assert.Contains("Git.Git", command.Arguments);
        Assert.Contains("--exact", command.Arguments);
        Assert.Contains("--source", command.Arguments);
        Assert.Contains("winget", command.Arguments);
        Assert.Contains("--download-directory", command.Arguments);
        Assert.Contains(output, command.Arguments);
        Assert.DoesNotContain("--ignore-security-hash", command.Arguments);
        Assert.DoesNotContain("--force", command.Arguments);
        Assert.DoesNotContain("--override", command.Arguments);
    }

    [Fact]
    public async Task PrepareAndInstallPreparedAsync_RechecksHashAndExecutesPreparedInstaller()
    {
        Directory.CreateDirectory(_root);
        var runner = new PreparationRunner("Git.Git");
        var provider = new WinGetProvider(
            new FakeLocator("C:\\Trusted\\winget.exe"),
            runner,
            _root);
        var request = Request("git", "Git.Git");

        var preparation = await provider.PrepareAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken);
        var operation = await provider.InstallPreparedAsync(
            request,
            preparation,
            TestContext.Current.CancellationToken);

        Assert.True(preparation.IsReady);
        Assert.Equal(PackageOperationStatus.Succeeded, operation.Status);
        Assert.Single(runner.PreparedInstallerCalls);
        Assert.DoesNotContain(
            runner.PreparedInstallerCalls[0].Arguments,
            argument => argument.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallPreparedAsync_BlocksInstallerThatChangesAfterPreparation()
    {
        Directory.CreateDirectory(_root);
        var runner = new PreparationRunner("Git.Git");
        var provider = new WinGetProvider(
            new FakeLocator("C:\\Trusted\\winget.exe"),
            runner,
            _root);
        var request = Request("git", "Git.Git");

        var preparation = await provider.PrepareAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(preparation.IsReady);

        var installer = Assert.Single(
            Directory.EnumerateFiles(_root, "*.exe", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(
            installer,
            "tampered",
            TestContext.Current.CancellationToken);

        var operation = await provider.InstallPreparedAsync(
            request,
            preparation,
            TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.IntegrityFailure, operation.Status);
        Assert.Equal("winget.prepared-installer-hash-mismatch", operation.DiagnosticCode);
        Assert.Empty(runner.PreparedInstallerCalls);
    }

    [Fact]
    public void CanPrepare_DoesNotPreDownloadMicrosoftStorePackages()
    {
        var provider = new WinGetProvider(
            new FakeLocator("C:\\Trusted\\winget.exe"),
            new PreparationRunner("Example.Store"),
            _root);
        var storePackage = new ProviderPackageReference(
            PackageProviderIds.WinGet,
            "9NBLGGH4NNS1",
            "msstore");

        Assert.False(provider.CanPrepare(storePackage));
    }

    private static PackageInstallRequest Request(string applicationId, string packageId) =>
        new(
            applicationId,
            new ProviderPackageReference(
                PackageProviderIds.WinGet,
                packageId,
                "winget"),
            Silent: true,
            AcceptPackageAgreements: true,
            AcceptSourceAgreements: true);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FakeLocator(string path) : IWinGetExecutableLocator
    {
        public WinGetExecutableResolution Resolve() => new(true, path);
    }

    private sealed class PreparationRunner(string packageId) : IWinGetProcessRunner
    {
        public List<Call> PreparedInstallerCalls { get; } = [];

        public Task<WinGetProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (arguments.Count > 0 &&
                string.Equals(arguments[0], "download", StringComparison.OrdinalIgnoreCase))
            {
                var directoryIndex = IndexOf(arguments, "--download-directory");
                Assert.True(directoryIndex >= 0);
                var directory = arguments[directoryIndex + 1];
                Directory.CreateDirectory(directory);

                var installerPath = Path.Combine(directory, "installer.exe");
                var installerBytes = Encoding.UTF8.GetBytes("trusted prepared installer");
                File.WriteAllBytes(installerPath, installerBytes);
                var hash = Convert.ToHexString(SHA256.HashData(installerBytes));
                File.WriteAllText(
                    Path.Combine(directory, "manifest.yaml"),
                    $"""
                    PackageIdentifier: {packageId}
                    InstallerType: exe
                    InstallerSha256: {hash}
                    InstallerSwitches:
                      Silent: /S
                    InstallerSuccessCodes:
                      - 0
                    """);

                return Task.FromResult(new WinGetProcessResult(
                    true,
                    0,
                    string.Empty,
                    string.Empty));
            }

            PreparedInstallerCalls.Add(new Call(executablePath, arguments.ToArray()));
            return Task.FromResult(new WinGetProcessResult(
                true,
                0,
                string.Empty,
                string.Empty));
        }

        private static int IndexOf(IReadOnlyList<string> values, string expected)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private sealed record Call(
        string ExecutablePath,
        IReadOnlyList<string> Arguments);
}
