using AgenStart.Core.Machine;
using AgenStart.Platform.Windows.Inventory;
using AgenStart.Platform.Windows.WinGet;

namespace AgenStart.Platform.Windows.Tests;

public sealed class WindowsMachineInventoryProviderTests
{
    [Theory]
    [InlineData("v1.10.340", 1, 10, 340)]
    [InlineData("1.9.25200", 1, 9, 25200)]
    [InlineData("v1.11.0-preview", 1, 11, 0)]
    [InlineData("  v2.0.1\r\n", 2, 0, 1)]
    public void ParseWinGetVersion_normalizes_supported_output(
        string output,
        int major,
        int minor,
        int build)
    {
        var version = WindowsMachineInventoryProvider.ParseWinGetVersion(output);

        Assert.NotNull(version);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("vnext")]
    public void ParseWinGetVersion_rejects_unknown_output(string output)
    {
        Assert.Null(WindowsMachineInventoryProvider.ParseWinGetVersion(output));
    }

    [Fact]
    public async Task CaptureAsync_returns_safe_snapshot_on_non_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsMachineInventoryProvider(
            new FakeLocator(),
            new FakeRunner());

        var snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PlatformKind.Unknown, snapshot.Platform.Kind);
        Assert.Equal(PackageManagerKind.None, snapshot.PackageManager.Kind);
        Assert.Equal(CapabilityState.Unavailable, snapshot.PackageManager.State);
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == "inventory.unsupported-platform");
    }

    private sealed class FakeLocator : IWinGetExecutableLocator
    {
        public WinGetExecutableResolution Resolve() =>
            new(true, "C:\\WindowsApps\\winget.exe");
    }

    private sealed class FakeRunner : IWinGetProcessRunner
    {
        public Task<WinGetProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WinGetProcessResult(true, 0, "v1.10.340", string.Empty));
    }
}
