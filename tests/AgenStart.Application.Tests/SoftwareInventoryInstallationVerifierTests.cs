using AgenStart.Application.Installation;
using AgenStart.PackageManagement;
using AgenStart.SoftwareInventory;

namespace AgenStart.Application.Tests;

public sealed class SoftwareInventoryInstallationVerifierTests
{
    [Fact]
    public async Task VerifyAsync_UsesNormalizedInventoryStateAndReturnsInstalledVersion()
    {
        var package = new ProviderPackageReference(
            PackageProviderIds.WinGet,
            "Git.Git",
            "winget");
        var target = new SoftwareDetectionTarget(
            "git",
            "Git",
            "Git Project",
            [package],
            ["Git"]);
        var sourceId = SoftwareInventorySourceIds.ForPackageProvider(
            PackageProviderIds.WinGet,
            "winget");
        var snapshot = new InstalledSoftwareSnapshot(
            [
                new InstalledSoftwareRecord(
                    InstalledSoftwareSourceKind.PackageProvider,
                    sourceId,
                    "Git.Git",
                    Version: "2.51.0",
                    ProviderId: PackageProviderIds.WinGet,
                    PackageId: "Git.Git",
                    PackageSource: "winget")
            ],
            [new InventorySourceStatus(sourceId, InventorySourceState.Complete)],
            DateTimeOffset.UnixEpoch);
        var verifier = new SoftwareInventoryInstallationVerifier(
            new FakeInventoryProvider(snapshot),
            [target]);

        var result = await verifier.VerifyAsync(
            "git",
            TestContext.Current.CancellationToken);

        Assert.Equal(InstallationVerificationStatus.Verified, result.Status);
        Assert.Equal("2.51.0", result.InstalledVersion);
    }

    [Fact]
    public async Task VerifyAsync_DoesNotConvertIncompleteInventoryIntoMissing()
    {
        var package = new ProviderPackageReference(
            PackageProviderIds.WinGet,
            "Git.Git",
            "winget");
        var target = new SoftwareDetectionTarget(
            "git",
            "Git",
            "Git Project",
            [package],
            ["Git"]);
        var sourceId = SoftwareInventorySourceIds.ForPackageProvider(
            PackageProviderIds.WinGet,
            "winget");
        var snapshot = new InstalledSoftwareSnapshot(
            [],
            [new InventorySourceStatus(sourceId, InventorySourceState.Partial)],
            DateTimeOffset.UnixEpoch);
        var verifier = new SoftwareInventoryInstallationVerifier(
            new FakeInventoryProvider(snapshot),
            [target]);

        var result = await verifier.VerifyAsync(
            "git",
            TestContext.Current.CancellationToken);

        Assert.Equal(InstallationVerificationStatus.Unknown, result.Status);
        Assert.Equal("inventory.insufficient-evidence", result.DiagnosticCode);
    }

    private sealed class FakeInventoryProvider(InstalledSoftwareSnapshot snapshot)
        : IInstalledSoftwareInventoryProvider
    {
        public Task<InstalledSoftwareSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(snapshot);
        }
    }
}
