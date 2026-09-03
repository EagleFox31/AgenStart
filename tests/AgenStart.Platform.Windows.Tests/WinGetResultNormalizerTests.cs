using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.WinGet;

namespace AgenStart.Platform.Windows.Tests;

public sealed class WinGetResultNormalizerTests
{
    [Fact]
    public void Normalize_MapsSuccess()
    {
        var result = WinGetResultNormalizer.Normalize(ProcessResult(0));

        Assert.Equal(PackageOperationStatus.Succeeded, result.Status);
        Assert.Equal("winget.success", result.DiagnosticCode);
    }

    [Theory]
    [InlineData(0x8A150061u)]
    [InlineData(0x8A15010Du)]
    public void Normalize_MapsAlreadyInstalled(uint hresult)
    {
        var result = WinGetResultNormalizer.Normalize(ProcessResult(HResult(hresult)));

        Assert.Equal(PackageOperationStatus.AlreadyInstalled, result.Status);
    }

    [Theory]
    [InlineData(0x8A150011u)]
    [InlineData(0x8A15002Du)]
    [InlineData(0x8A15003Fu)]
    [InlineData(0x8A15005Eu)]
    [InlineData(0x8A150060u)]
    public void Normalize_MapsIntegrityFailures(uint hresult)
    {
        var result = WinGetResultNormalizer.Normalize(ProcessResult(HResult(hresult)));

        Assert.Equal(PackageOperationStatus.IntegrityFailure, result.Status);
    }

    [Theory]
    [InlineData(0x8A150041u)]
    [InlineData(0x8A150046u)]
    public void Normalize_MapsAgreementRequirements(uint hresult)
    {
        var result = WinGetResultNormalizer.Normalize(ProcessResult(HResult(hresult)));

        Assert.Equal(PackageOperationStatus.AgreementRequired, result.Status);
    }

    [Fact]
    public void Normalize_CancellationWinsOverNativeExitCode()
    {
        var result = WinGetResultNormalizer.Normalize(
            new WinGetProcessResult(
                true,
                HResult(0x8A150003u),
                string.Empty,
                string.Empty,
                Cancelled: true));

        Assert.Equal(PackageOperationStatus.Cancelled, result.Status);
        Assert.Equal("provider.cancelled", result.DiagnosticCode);
    }

    [Fact]
    public void Normalize_TimeoutWinsOverNativeExitCode()
    {
        var result = WinGetResultNormalizer.Normalize(
            new WinGetProcessResult(
                true,
                null,
                string.Empty,
                string.Empty,
                TimedOut: true));

        Assert.Equal(PackageOperationStatus.TimedOut, result.Status);
    }

    [Fact]
    public void Normalize_UnknownCodeFailsClosed()
    {
        var result = WinGetResultNormalizer.Normalize(ProcessResult(123456));

        Assert.Equal(PackageOperationStatus.Failed, result.Status);
        Assert.Equal("winget.unmapped-error", result.DiagnosticCode);
    }

    private static WinGetProcessResult ProcessResult(int exitCode) =>
        new(true, exitCode, string.Empty, string.Empty);

    private static int HResult(uint value) => unchecked((int)value);
}
