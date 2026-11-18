using System.Security.Cryptography;
using System.Text;
using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.WinGet;

namespace AgenStart.Platform.Windows.Tests;

public sealed class WinGetProgressParserTests : IDisposable
{
    private const long Kb = 1024;
    private const long Mb = 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AgenStartTests",
        Guid.NewGuid().ToString("N"));

    public static TheoryData<string, long, long> ByteLines => new()
    {
        { "  ███████████▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  38.0 MB /  104 MB", 38 * Mb, 104 * Mb },
        { "  ██████████████████████████████   104 MB /  104 MB", 104 * Mb, 104 * Mb },
        { "  ▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  1024 KB / 2.00 MB", 1024 * Kb, 2 * Mb },
        { "\u001b[2K  ████▒▒▒▒  12.5 MB / 50.0 MB\u001b[0m", (long)(12.5 * Mb), 50 * Mb },
        { "  ΓûêΓûêΓûêΓûÆΓûÆ  5.00 MB / 10.0 MB", 5 * Mb, 10 * Mb },
        { "  ████▒▒▒▒  1,50 Go / 3,00 Go", 1536 * Mb, 3072 * Mb },
        { "  ████▒▒▒▒  0 B / 512 KB", 0, 512 * Kb },
    };

    [Theory]
    [MemberData(nameof(ByteLines))]
    public void TryParse_ReadsByteProgressFromRealWinGetLines(string line, long downloaded, long required)
    {
        Assert.True(WinGetProgressParser.TryParse(line, out var progress));
        Assert.Equal(downloaded, progress.BytesDownloaded);
        Assert.Equal(required, progress.BytesRequired);
        Assert.Equal((double)downloaded / required, progress.Fraction, 6);
    }

    [Theory]
    [InlineData("  ██████████▒▒▒▒▒▒▒▒▒▒  50%", 0.5)]
    [InlineData("  ██████████████████████████████  100%", 1)]
    [InlineData("  ▒▒▒▒▒▒▒▒▒▒  0%", 0)]
    [InlineData("  ███▒▒▒▒▒▒▒  33.5 %", 0.335)]
    public void TryParse_ReadsPercentOnlyProgress(string line, double fraction)
    {
        Assert.True(WinGetProgressParser.TryParse(line, out var progress));
        Assert.Null(progress.BytesDownloaded);
        Assert.Null(progress.BytesRequired);
        Assert.Equal(fraction, progress.Fraction, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   - ")]
    [InlineData("   \\ ")]
    [InlineData("Found PowerShell [Microsoft.PowerShell] Version 7.4.6.0")]
    [InlineData("This application is licensed to you by its owner.")]
    [InlineData("Downloading https://github.com/PowerShell/PowerShell/releases/download/v7.4.6/PowerShell-7.4.6-win-x64.msi")]
    [InlineData("Downloading https://example.test/files/setup%2Fx64%20build.exe")]
    [InlineData("Successfully verified installer hash")]
    [InlineData("Installer downloaded: C:\\Temp\\AgenStart\\Microsoft.PowerShell_7.4.6.0_Machine_X64_wix_en-US.msi")]
    [InlineData("  ████  ab MB / cd MB")]
    [InlineData("  ████  10 MB / 0 B")]
    [InlineData("  ████  150%")]
    public void TryParse_RejectsLinesWithoutReadableProgress(string? line)
    {
        Assert.False(WinGetProgressParser.TryParse(line, out _));
    }

    [Fact]
    public void TryParse_ClampsDownloadedBytesToTheTotal()
    {
        Assert.True(WinGetProgressParser.TryParse("  ████  10.1 MB / 10.0 MB", out var progress));

        Assert.Equal(progress.BytesRequired, progress.BytesDownloaded);
        Assert.Equal(1, progress.Fraction);
    }

    [Fact]
    public void Throttle_IgnoresProgressThatGoesBackwardsWithinTheSameTransfer()
    {
        var throttle = new WinGetProgressThrottle(TimeSpan.FromMilliseconds(250));
        var interval = TimeSpan.FromSeconds(1);

        Assert.True(throttle.TryAccept(Sample(50, 100), interval * 1));
        Assert.False(throttle.TryAccept(Sample(40, 100), interval * 2));
        Assert.False(throttle.TryAccept(Sample(50, 100), interval * 3));
        Assert.True(throttle.TryAccept(Sample(60, 100), interval * 4));
    }

    [Fact]
    public void Throttle_AcceptsANewTransferEvenWhenItsFiguresAreLower()
    {
        var throttle = new WinGetProgressThrottle(TimeSpan.FromMilliseconds(250));

        Assert.True(throttle.TryAccept(Sample(90, 100), TimeSpan.FromSeconds(1)));
        Assert.True(throttle.TryAccept(Sample(1, 20), TimeSpan.FromSeconds(1.01)));
        Assert.False(throttle.TryAccept(Sample(2, 20), TimeSpan.FromSeconds(1.02)));
    }

    [Fact]
    public void Throttle_LimitsReportsToTheIntervalButAlwaysLetsCompletionThrough()
    {
        var throttle = new WinGetProgressThrottle(TimeSpan.FromMilliseconds(250));

        Assert.True(throttle.TryAccept(Sample(10, 100), TimeSpan.FromMilliseconds(0)));
        Assert.False(throttle.TryAccept(Sample(20, 100), TimeSpan.FromMilliseconds(100)));
        Assert.True(throttle.TryAccept(Sample(30, 100), TimeSpan.FromMilliseconds(300)));
        Assert.True(throttle.TryAccept(Sample(100, 100), TimeSpan.FromMilliseconds(310)));
    }

    [Fact]
    public async Task PumpAsync_SplitsOnCarriageReturnsAndNewLinesAndKeepsTheFullOutput()
    {
        const string output = "Found Git\r\n   - \r   \\ \r  ██▒▒  1.00 MB / 2.00 MB\r  ████  2.00 MB / 2.00 MB\nDone";
        var lines = new List<string>();

        var all = await WinGetOutputLines.PumpAsync(
            new StringReader(output),
            lines.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(output, all);
        Assert.Equal(
            ["Found Git", "  ██▒▒  1.00 MB / 2.00 MB", "  ████  2.00 MB / 2.00 MB", "Done"],
            lines.Where(line => line.Trim().Length > 1));
        Assert.DoesNotContain(lines, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public async Task PrepareAsync_ReportsStreamedDownloadProgressInBytes()
    {
        Directory.CreateDirectory(_root);
        var runner = new StreamingDownloadRunner(
            "Microsoft.PowerShell",
            [
                "Found PowerShell [Microsoft.PowerShell] Version 7.4.6.0",
                "  ███████████▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  38.0 MB /  104 MB",
                "  ███████████▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒  30.0 MB /  104 MB",
                "  ██████████████████████████████   104 MB /  104 MB",
                "Successfully verified installer hash"
            ]);
        var provider = new WinGetProvider(new FakeLocator("C:\\Trusted\\winget.exe"), runner, _root);
        var reports = new List<PackagePreparationProgress>();

        var preparation = await provider.PrepareAsync(
            Request("pwsh", "Microsoft.PowerShell"),
            new SynchronousProgress(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.True(preparation.IsReady);
        Assert.True(runner.ReceivedLineCallback);
        Assert.Equal(0, reports[0].BytesDownloaded);
        Assert.Contains(reports, report => report.BytesDownloaded == 38 * Mb && report.BytesRequired == 104 * Mb);
        Assert.Contains(reports, report => report.BytesDownloaded == 104 * Mb && report.Fraction == 1);
        Assert.DoesNotContain(reports, report => report.BytesDownloaded == 30 * Mb);
        Assert.Equal(1, reports[^1].Fraction);
    }

    [Fact]
    public async Task RunnerWithoutStreaming_FallsBackToTheBufferedCall()
    {
        IWinGetProcessRunner runner = new BufferedOnlyRunner();
        var lines = new List<string>();

        var result = await runner.RunAsync(
            "winget.exe",
            ["--version"],
            TimeSpan.FromSeconds(1),
            lines.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal("v1.9.0", result.StandardOutput);
        Assert.Empty(lines);
    }

    private static WinGetDownloadProgress Sample(long downloaded, long required) =>
        new(downloaded, required, (double)downloaded / required);

    private static PackageInstallRequest Request(string applicationId, string packageId) =>
        new(
            applicationId,
            new ProviderPackageReference(PackageProviderIds.WinGet, packageId, "winget"),
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

    private sealed class SynchronousProgress(Action<PackagePreparationProgress> report)
        : IProgress<PackagePreparationProgress>
    {
        public void Report(PackagePreparationProgress value) => report(value);
    }

    private sealed class FakeLocator(string path) : IWinGetExecutableLocator
    {
        public WinGetExecutableResolution Resolve() => new(true, path);
    }

    private sealed class BufferedOnlyRunner : IWinGetProcessRunner
    {
        public Task<WinGetProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WinGetProcessResult(true, 0, "v1.9.0", string.Empty));
    }

    /// <summary>Writes a fake prepared package and replays recorded WinGet lines; never starts a process.</summary>
    private sealed class StreamingDownloadRunner(string packageId, IReadOnlyList<string> lines) : IWinGetProcessRunner
    {
        public bool ReceivedLineCallback { get; private set; }

        public Task<WinGetProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            RunAsync(executablePath, arguments, timeout, onOutputLine: null, cancellationToken);

        public Task<WinGetProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            Action<string>? onOutputLine,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("download", arguments[0]);
            ReceivedLineCallback = onOutputLine is not null;

            var directory = arguments[arguments.ToList().IndexOf("--download-directory") + 1];
            Directory.CreateDirectory(directory);
            var installerBytes = Encoding.UTF8.GetBytes("trusted prepared installer");
            File.WriteAllBytes(Path.Combine(directory, "installer.exe"), installerBytes);
            File.WriteAllText(
                Path.Combine(directory, "manifest.yaml"),
                $"""
                PackageIdentifier: {packageId}
                InstallerType: exe
                InstallerSha256: {Convert.ToHexString(SHA256.HashData(installerBytes))}
                InstallerSwitches:
                  Silent: /S
                InstallerSuccessCodes:
                  - 0
                """);

            foreach (var line in lines)
            {
                onOutputLine?.Invoke(line);
            }

            return Task.FromResult(new WinGetProcessResult(true, 0, string.Join('\n', lines), string.Empty));
        }
    }
}
