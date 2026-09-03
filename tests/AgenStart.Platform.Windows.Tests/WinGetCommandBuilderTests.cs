using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.WinGet;

namespace AgenStart.Platform.Windows.Tests;

public sealed class WinGetCommandBuilderTests
{
    [Fact]
    public void BuildInstall_UsesExactTrustedPackageAndNoUpgrade()
    {
        var request = new PackageInstallRequest(
            "visual-studio-code",
            new ProviderPackageReference(
                PackageProviderIds.WinGet,
                "Microsoft.VisualStudioCode",
                "winget",
                PackageScope.User));

        var command = WinGetCommandBuilder.BuildInstall(request);

        Assert.Equal(
        [
            "install",
            "--id", "Microsoft.VisualStudioCode",
            "--exact",
            "--source", "winget",
            "--disable-interactivity",
            "--no-upgrade",
            "--scope", "user",
            "--silent"
        ],
        command.Arguments);
    }

    [Fact]
    public void BuildInstall_AddsAgreementFlagsOnlyAfterExplicitConsent()
    {
        var request = new PackageInstallRequest(
            "powertoys",
            new ProviderPackageReference(
                PackageProviderIds.WinGet,
                "Microsoft.PowerToys",
                "winget"),
            AcceptPackageAgreements: true,
            AcceptSourceAgreements: true);

        var command = WinGetCommandBuilder.BuildInstall(request);

        Assert.Contains("--accept-package-agreements", command.Arguments);
        Assert.Contains("--accept-source-agreements", command.Arguments);
    }

    [Fact]
    public void BuildInstall_DoesNotExposeSecurityBypassArguments()
    {
        var request = new PackageInstallRequest(
            "git",
            new ProviderPackageReference(PackageProviderIds.WinGet, "Git.Git", "winget"));

        var command = WinGetCommandBuilder.BuildInstall(request);

        var prohibited = new[]
        {
            "--override",
            "--custom",
            "--force",
            "--ignore-security-hash",
            "--ignore-local-archive-malware-scan",
            "--allow-reboot"
        };

        Assert.DoesNotContain(command.Arguments, argument => prohibited.Contains(argument));
    }

    [Theory]
    [InlineData("--override")]
    [InlineData("Microsoft.VisualStudioCode --force")]
    [InlineData("Microsoft.VisualStudioCode;calc.exe")]
    [InlineData("")]
    public void BuildInstall_RejectsUnsafePackageIds(string packageId)
    {
        var request = new PackageInstallRequest(
            "visual-studio-code",
            new ProviderPackageReference(PackageProviderIds.WinGet, packageId, "winget"));

        Assert.Throws<ArgumentException>(() => WinGetCommandBuilder.BuildInstall(request));
    }

    [Theory]
    [InlineData("evil-source")]
    [InlineData("winget-preview")]
    [InlineData("https://example.test")]
    public void BuildInstall_RejectsUntrustedSources(string source)
    {
        var request = new PackageInstallRequest(
            "git",
            new ProviderPackageReference(PackageProviderIds.WinGet, "Git.Git", source));

        Assert.Throws<ArgumentException>(() => WinGetCommandBuilder.BuildInstall(request));
    }

    [Fact]
    public void BuildResolve_UsesExactIdAndConfiguredSource()
    {
        var package = new ProviderPackageReference(
            PackageProviderIds.WinGet,
            "VideoLAN.VLC",
            "winget");

        var command = WinGetCommandBuilder.BuildResolve(package);

        Assert.Equal(
        [
            "show",
            "--id", "VideoLAN.VLC",
            "--exact",
            "--source", "winget",
            "--disable-interactivity"
        ],
        command.Arguments);
    }
}
