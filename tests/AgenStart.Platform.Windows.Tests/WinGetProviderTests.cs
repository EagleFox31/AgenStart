using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.WinGet;

namespace AgenStart.Platform.Windows.Tests;

public sealed class WinGetProviderTests
{
    [Fact]
    public async Task ResolveAsync_DoesNotFallbackToAnotherSource()
    {
        var locator = new FakeLocator("C:\\Users\\test\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe");
        var runner = new RecordingRunner(
            new WinGetProcessResult(
                true,
                HResult(0x8A150045u),
                string.Empty,
                string.Empty));
        var provider = new WinGetProvider(locator, runner);
        var package = new ProviderPackageReference(
            PackageProviderIds.WinGet,
            "Microsoft.PowerToys",
            "winget");

        var result = await provider.ResolveAsync(
            package,
            TestContext.Current.CancellationToken);

        Assert.Equal(PackageResolutionStatus.SourceUnavailable, result.Status);
        Assert.Single(runner.Calls);
        Assert.Contains("winget", runner.Calls[0].Arguments);
        Assert.DoesNotContain("msstore", runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task InstallAsync_InvalidReferenceNeverStartsProcess()
    {
        var locator = new FakeLocator("C:\\Users\\test\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe");
        var runner = new RecordingRunner(new WinGetProcessResult(true, 0, string.Empty, string.Empty));
        var provider = new WinGetProvider(locator, runner);
        var request = new PackageInstallRequest(
            "git",
            new ProviderPackageReference(
                PackageProviderIds.WinGet,
                "Git.Git --override",
                "winget"));

        var result = await provider.InstallAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.Failed, result.Status);
        Assert.Equal("winget.invalid-install-request", result.DiagnosticCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task InstallAsync_UsesStructuredResultWithoutReturningRawProviderOutput()
    {
        var locator = new FakeLocator("C:\\Users\\test\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe");
        var runner = new RecordingRunner(
            new WinGetProcessResult(
                true,
                HResult(0x8A150011u),
                "C:\\Users\\SensitiveUser\\Downloads\\payload.exe",
                "hash mismatch at sensitive path"));
        var provider = new WinGetProvider(locator, runner);
        var request = new PackageInstallRequest(
            "git",
            new ProviderPackageReference(PackageProviderIds.WinGet, "Git.Git", "winget"));

        var result = await provider.InstallAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.IntegrityFailure, result.Status);
        Assert.DoesNotContain("SensitiveUser", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload.exe", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UninstallAsync_InvalidReferenceNeverStartsProcess()
    {
        var runner = new RecordingRunner(new WinGetProcessResult(true, 0, string.Empty, string.Empty));
        var provider = new WinGetProvider(new FakeLocator(@"C:\winget.exe"), runner);

        var result = await provider.UninstallAsync(
            new PackageUninstallRequest("git", new ProviderPackageReference(PackageProviderIds.WinGet, "Git.Git --force", "winget")),
            TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.Failed, result.Status);
        Assert.Equal("winget.invalid-uninstall-request", result.DiagnosticCode);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(0u, PackageOperationStatus.Succeeded, "winget.success")]
    [InlineData(0x8A150014u, PackageOperationStatus.NotFound, "winget.no-applications-found")]
    [InlineData(0x8A150102u, PackageOperationStatus.Failed, "winget.install-in-progress")]
    [InlineData(0x8A15010Cu, PackageOperationStatus.CancelledByUser, "winget.install-cancelled-by-user")]
    public async Task UninstallAsync_RunsTheBuiltCommandThroughTheInjectedRunner(uint exitCode, PackageOperationStatus status, string? diagnostic)
    {
        var runner = new RecordingRunner(new WinGetProcessResult(true, HResult(exitCode), string.Empty, string.Empty));
        var provider = new WinGetProvider(new FakeLocator(@"C:\winget.exe"), runner);
        var request = new PackageUninstallRequest("git", new ProviderPackageReference(PackageProviderIds.WinGet, "Git.Git", "winget"));

        var result = await provider.UninstallAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(diagnostic, result.DiagnosticCode);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(@"C:\winget.exe", call.ExecutablePath);
        Assert.Equal(WinGetCommandBuilder.BuildUninstall(request).Arguments, call.Arguments);
    }

    private static int HResult(uint value) => unchecked((int)value);

    private sealed class FakeLocator(string path) : IWinGetExecutableLocator
    {
        public WinGetExecutableResolution Resolve() => new(true, path);
    }

    private sealed class RecordingRunner(WinGetProcessResult response) : IWinGetProcessRunner
    {
        public List<Call> Calls { get; } = [];

        public Task<WinGetProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(executablePath, arguments.ToArray(), timeout));
            return Task.FromResult(response);
        }
    }

    private sealed record Call(
        string ExecutablePath,
        IReadOnlyList<string> Arguments,
        TimeSpan Timeout);
}
