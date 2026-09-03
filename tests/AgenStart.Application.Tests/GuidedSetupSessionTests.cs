using AgenStart.Application.GuidedSetup;
using AgenStart.Application.Installation;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.PackageManagement;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Application.Tests;

public sealed class GuidedSetupSessionTests
{
    [Fact]
    public async Task GuidedFlow_DoesNotInvokeInstallerBeforeExplicitConfirmation()
    {
        var provider = new TrackingProvider();
        var verifier = new StatefulVerifier();
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        using var session = CreateSession(provider, verifier, orchestrator, SoftwarePresenceState.Missing);

        session.Continue();
        session.Continue();
        session.SelectProfile(UserProfile.Development);
        session.Continue();
        session.Continue();

        Assert.Equal(GuidedSetupStep.Confirmation, session.Step);
        Assert.False(session.InstallationConfirmed);
        Assert.Equal(0, provider.InstallCount);
        Assert.Null(session.InstallationReport);

        await session.ConfirmAndInstallAsync(TestContext.Current.CancellationToken);

        Assert.True(session.InstallationConfirmed);
        Assert.Equal(1, provider.InstallCount);
        Assert.Equal(GuidedSetupStep.Report, session.Step);
        Assert.Equal(1, session.InstallationReport?.SucceededCount);
    }

    [Fact]
    public void GuidedFlow_AlreadyInstalledRecommendationIsVisibleButCannotBeSelected()
    {
        var provider = new TrackingProvider();
        var verifier = new StatefulVerifier(installed: true);
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        using var session = CreateSession(provider, verifier, orchestrator, SoftwarePresenceState.Installed);

        session.Continue();
        session.Continue();
        session.SelectProfile(UserProfile.Development);

        var item = Assert.Single(session.Recommendations);
        Assert.Equal(RecommendationDisposition.AlreadyInstalled, item.Decision.Disposition);
        Assert.False(item.CanSelect);
        Assert.False(item.IsSelected);
        Assert.Throws<InvalidOperationException>(() => session.SetSelected("git", true));
    }

    [Fact]
    public void GuidedFlow_IncompatibleRecommendationCannotEnterUserSelection()
    {
        var provider = new TrackingProvider();
        var verifier = new StatefulVerifier();
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        var lowMemoryMachine = Machine(totalRamMiB: 128);
        using var session = new GuidedSetupSession(
            lowMemoryMachine,
            Software(SoftwarePresenceState.Missing),
            [Candidate()],
            new RecommendationEngine(),
            orchestrator);

        session.Continue();
        session.Continue();
        session.SelectProfile(UserProfile.Development);

        var item = Assert.Single(session.Recommendations);
        Assert.Equal(RecommendationDisposition.Incompatible, item.Decision.Disposition);
        Assert.False(item.CanSelect);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public async Task GuidedFlow_UserCanRemoveDefaultRecommendationBeforeConfirmation()
    {
        var provider = new TrackingProvider();
        var verifier = new StatefulVerifier();
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        using var session = CreateSession(provider, verifier, orchestrator, SoftwarePresenceState.Missing);

        session.Continue();
        session.Continue();
        session.SelectProfile(UserProfile.Development);
        Assert.True(Assert.Single(session.Recommendations).IsSelected);

        session.SetSelected("git", false);
        session.Continue();
        session.Continue();
        await session.ConfirmAndInstallAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, provider.InstallCount);
        Assert.Equal(0, session.InstallationReport?.Items.Count);
    }

    [Fact]
    public void GuidedFlow_ProfileSelectionBuildsHumanReadableRecommendationReasons()
    {
        var provider = new TrackingProvider();
        var verifier = new StatefulVerifier();
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        using var session = CreateSession(provider, verifier, orchestrator, SoftwarePresenceState.Missing);

        session.Continue();
        session.Continue();
        session.SelectProfile(UserProfile.Development);

        var item = Assert.Single(session.Recommendations);
        Assert.NotEmpty(item.Decision.Reasons);
        Assert.All(item.Decision.Reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason.Message)));
    }

    private static GuidedSetupSession CreateSession(
        TrackingProvider provider,
        StatefulVerifier verifier,
        InstallationOrchestrator orchestrator,
        SoftwarePresenceState state) =>
        new(
            Machine(),
            Software(state),
            [Candidate()],
            new RecommendationEngine(),
            orchestrator);

    private static GuidedApplicationCandidate Candidate() =>
        new(
            new ApplicationDefinition(
                "git",
                "Git",
                ApplicationLifecycleStatus.Active,
                [new ProfileRecommendation(UserProfile.Development, RecommendationLevel.Essential, "development.source-control")],
                new ApplicationRequirements(
                    new CapabilityRequirements(512, 200, false, [MachineArchitecture.X64]),
                    new CapabilityRequirements(4096, 500, false, [MachineArchitecture.X64])),
                [new PlatformSupportRule(PlatformKind.Windows, PlatformSupportStatus.Supported, [MachineArchitecture.X64])],
                [],
                []),
            new ProviderPackageReference(PackageProviderIds.WinGet, "Git.Git", "winget"));

    private static MachineSnapshot Machine(long totalRamMiB = 16 * 1024) =>
        new(
            new PlatformSnapshot(
                PlatformKind.Windows,
                "Windows 11 Pro",
                "24H2",
                new Version(10, 0, 26100),
                "26100",
                "0",
                MachineArchitecture.X64,
                MachineArchitecture.X64),
            new CpuSnapshot("Prototype CPU", MachineArchitecture.X64, 12),
            new MemorySnapshot((ulong)totalRamMiB * 1024UL * 1024UL, 8UL * 1024UL * 1024UL * 1024UL),
            [new GpuSnapshot("Prototype GPU", "Prototype")],
            [new StorageSnapshot("C:\\", StorageKind.Fixed, 512L * 1024 * 1024 * 1024, 128L * 1024 * 1024 * 1024, true)],
            new PackageManagerSnapshot(PackageManagerKind.WinGet, CapabilityState.Available, new Version(1, 12)),
            new CapabilitySnapshot(GpuCapabilityState.Available),
            [],
            DateTimeOffset.UtcNow);

    private static SoftwareDetectionResult Software(SoftwarePresenceState state) =>
        new(
            [new DetectedApplicationState("git", state, state == SoftwarePresenceState.Installed ? "2.51.0" : null, [], [])],
            0);

    private sealed class StatefulVerifier(bool installed = false) : IInstallationVerifier
    {
        private bool _installed = installed;

        public Task<InstallationVerificationResult> VerifyAsync(
            string applicationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_installed
                ? new InstallationVerificationResult(InstallationVerificationStatus.Verified, "2.51.0")
                : new InstallationVerificationResult(InstallationVerificationStatus.NotInstalled));
        }

        public void MarkInstalled() => _installed = true;
    }

    private sealed class TrackingProvider : IPackageProvider
    {
        public string ProviderId => PackageProviderIds.WinGet;
        public int InstallCount { get; private set; }
        public StatefulVerifier? Verifier { get; set; }

        public Task<PackageProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PackageProviderAvailability(PackageProviderAvailabilityStatus.Available, "1.12.0"));

        public Task<PackageResolutionResult> ResolveAsync(
            ProviderPackageReference package,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PackageResolutionResult(PackageResolutionStatus.Resolved, package));

        public Task<PackageOperationResult> InstallAsync(
            PackageInstallRequest request,
            CancellationToken cancellationToken = default)
        {
            InstallCount++;
            return Task.FromResult(new PackageOperationResult(
                PackageOperationStatus.Succeeded,
                request.ApplicationId,
                request.Package));
        }
    }
}
