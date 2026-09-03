using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;
using Xunit;

namespace AgenStart.Recommendations.Tests;

public sealed class RecommendationEngineTests
{
    private readonly RecommendationEngine _engine = new();

    [Theory]
    [InlineData(UserProfile.Personal)]
    [InlineData(UserProfile.Development)]
    [InlineData(UserProfile.Business)]
    [InlineData(UserProfile.Creation)]
    [InlineData(UserProfile.Training)]
    public void Build_SupportsEveryInitialProfileWithHumanReadableReason(UserProfile profile)
    {
        var application = Application(
            $"{profile.ToString().ToLowerInvariant()}-tool",
            $"{profile} Tool",
            profile,
            RecommendationLevel.Recommended);

        var plan = _engine.Build(Request(
            profile,
            CapableMachine(),
            [Missing(application.Id)],
            [application]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.Recommended, decision.Disposition);
        Assert.True(decision.SelectedByDefault);
        Assert.False(string.IsNullOrWhiteSpace(decision.ProfileReasonKey));
        Assert.Contains(
            decision.Reasons,
            reason => reason.Message.Contains(profile.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Build_AlreadyInstalledApplicationIsExplicitAndNotSelected()
    {
        var application = Application(
            "git",
            "Git",
            UserProfile.Development,
            RecommendationLevel.Essential);

        var plan = _engine.Build(Request(
            UserProfile.Development,
            CapableMachine(),
            [Installed("git", "2.51.0")],
            [application]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.AlreadyInstalled, decision.Disposition);
        Assert.False(decision.SelectedByDefault);
        Assert.Contains(
            decision.Reasons,
            reason => reason.Code == "software.already-installed" &&
                      reason.Message.Contains("2.51.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_UnknownInstalledStateDoesNotCreateDuplicateInstallProposal()
    {
        var application = Application(
            "visual-studio-code",
            "Visual Studio Code",
            UserProfile.Development,
            RecommendationLevel.Recommended);

        var plan = _engine.Build(Request(
            UserProfile.Development,
            CapableMachine(),
            [Unknown("visual-studio-code")],
            [application]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.InventoryUnknown, decision.Disposition);
        Assert.False(decision.SelectedByDefault);
    }

    [Fact]
    public void Build_InsufficientMinimumRamMakesRecommendationIncompatible()
    {
        var application = Application(
            "docker-desktop",
            "Docker Desktop",
            UserProfile.Development,
            RecommendationLevel.Optional,
            minimum: Requirements(minRamMiB: 8192, minStorageMiB: 8192));

        var plan = _engine.Build(Request(
            UserProfile.Development,
            CapableMachine(totalRamMiB: 4096, freeStorageMiB: 32_000),
            [Missing(application.Id)],
            [application]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.Incompatible, decision.Disposition);
        Assert.Contains(decision.Reasons, reason => reason.Code == "capability.ram-insufficient");
    }

    [Fact]
    public void Build_UnknownRequiredCapabilityIsNotAssumedCompatible()
    {
        var application = Application(
            "docker-desktop",
            "Docker Desktop",
            UserProfile.Development,
            RecommendationLevel.Optional,
            minimum: Requirements(minRamMiB: 8192, minStorageMiB: 8192));

        var plan = _engine.Build(Request(
            UserProfile.Development,
            CapableMachine(totalRamMiB: null, freeStorageMiB: 32_000),
            [Missing(application.Id)],
            [application]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.CompatibilityUnknown, decision.Disposition);
        Assert.Contains(decision.Reasons, reason => reason.Code == "capability.ram-unknown");
    }

    [Fact]
    public void Build_GpuRequirementIsEnforced()
    {
        var application = Application(
            "obs-studio",
            "OBS Studio",
            UserProfile.Creation,
            RecommendationLevel.Recommended,
            minimum: Requirements(gpuRequired: true));

        var plan = _engine.Build(Request(
            UserProfile.Creation,
            CapableMachine(gpu: GpuCapabilityState.Unavailable),
            [Missing(application.Id)],
            [application]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.Incompatible, decision.Disposition);
        Assert.Contains(decision.Reasons, reason => reason.Code == "capability.gpu-required");
    }

    [Fact]
    public void Build_OptionalRecommendationIsVisibleButNotPreselected()
    {
        var application = Application(
            "firefox",
            "Mozilla Firefox",
            UserProfile.Development,
            RecommendationLevel.Optional);

        var decision = Assert.Single(_engine.Build(Request(
            UserProfile.Development,
            CapableMachine(),
            [Missing(application.Id)],
            [application])).Decisions);

        Assert.Equal(RecommendationDisposition.Recommended, decision.Disposition);
        Assert.False(decision.SelectedByDefault);
    }

    [Fact]
    public void Build_InstalledConflictBlocksCandidate()
    {
        var candidate = Application(
            "candidate",
            "Candidate",
            UserProfile.Business,
            RecommendationLevel.Recommended,
            conflicts: ["existing"]);
        var existing = Application(
            "existing",
            "Existing Tool",
            UserProfile.Personal,
            RecommendationLevel.Optional);

        var plan = _engine.Build(Request(
            UserProfile.Business,
            CapableMachine(),
            [Missing(candidate.Id), Installed(existing.Id, "1.0")],
            [candidate, existing]));

        var decision = Assert.Single(plan.Decisions);
        Assert.Equal(RecommendationDisposition.Conflict, decision.Disposition);
        Assert.False(decision.SelectedByDefault);
        Assert.Contains(decision.Reasons, reason => reason.Code == "conflict.installed-application");
    }

    [Fact]
    public void Build_ConflictingRecommendationsResolveDeterministicallyByLevel()
    {
        var essential = Application(
            "essential-tool",
            "Essential Tool",
            UserProfile.Development,
            RecommendationLevel.Essential,
            conflicts: ["optional-tool"]);
        var optional = Application(
            "optional-tool",
            "Optional Tool",
            UserProfile.Development,
            RecommendationLevel.Optional);

        var plan = _engine.Build(Request(
            UserProfile.Development,
            CapableMachine(),
            [Missing(essential.Id), Missing(optional.Id)],
            [optional, essential]));

        var winner = Assert.Single(plan.Decisions, decision => decision.ApplicationId == essential.Id);
        var loser = Assert.Single(plan.Decisions, decision => decision.ApplicationId == optional.Id);

        Assert.Equal(RecommendationDisposition.Recommended, winner.Disposition);
        Assert.True(winner.SelectedByDefault);
        Assert.Equal(RecommendationDisposition.Conflict, loser.Disposition);
        Assert.False(loser.SelectedByDefault);
        Assert.Contains(loser.Reasons, reason => reason.Code == "conflict.recommendation");
    }

    [Fact]
    public void Build_BelowRecommendedCapabilityAddsAdvisoryWithoutBlocking()
    {
        var application = Application(
            "editor",
            "Editor",
            UserProfile.Development,
            RecommendationLevel.Recommended,
            minimum: Requirements(minRamMiB: 2048),
            recommended: Requirements(minRamMiB: 8192));

        var decision = Assert.Single(_engine.Build(Request(
            UserProfile.Development,
            CapableMachine(totalRamMiB: 4096),
            [Missing(application.Id)],
            [application])).Decisions);

        Assert.Equal(RecommendationDisposition.Recommended, decision.Disposition);
        Assert.True(decision.SelectedByDefault);
        Assert.Contains(decision.Reasons, reason => reason.Code == "capability.ram-below-recommended");
    }

    [Fact]
    public void Build_DuplicateCanonicalApplicationIdFailsClosed()
    {
        var first = Application("same", "One", UserProfile.Personal, RecommendationLevel.Recommended);
        var second = Application("same", "Two", UserProfile.Personal, RecommendationLevel.Recommended);

        Assert.Throws<InvalidOperationException>(() =>
            _engine.Build(Request(
                UserProfile.Personal,
                CapableMachine(),
                [Missing("same")],
                [first, second])));
    }

    [Fact]
    public void Build_DuplicateProfileRuleFailsClosed()
    {
        var application = Application(
            "duplicate-rules",
            "Duplicate Rules",
            UserProfile.Personal,
            RecommendationLevel.Recommended) with
        {
            Recommendations =
            [
                new ProfileRecommendation(UserProfile.Personal, RecommendationLevel.Recommended, "personal.first"),
                new ProfileRecommendation(UserProfile.Personal, RecommendationLevel.Optional, "personal.second")
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            _engine.Build(Request(
                UserProfile.Personal,
                CapableMachine(),
                [Missing(application.Id)],
                [application])));
    }

    [Fact]
    public void Build_ApplicationOutsideSelectedProfileIsNotReturned()
    {
        var application = Application(
            "git",
            "Git",
            UserProfile.Development,
            RecommendationLevel.Essential);

        var plan = _engine.Build(Request(
            UserProfile.Business,
            CapableMachine(),
            [Missing(application.Id)],
            [application]));

        Assert.Empty(plan.Decisions);
    }

    private static RecommendationRequest Request(
        UserProfile profile,
        MachineSnapshot machine,
        IReadOnlyList<DetectedApplicationState> software,
        IReadOnlyList<ApplicationDefinition> applications) =>
        new(
            profile,
            machine,
            new SoftwareDetectionResult(software, 0),
            applications);

    private static ApplicationDefinition Application(
        string id,
        string name,
        UserProfile profile,
        RecommendationLevel level,
        CapabilityRequirements? minimum = null,
        CapabilityRequirements? recommended = null,
        IReadOnlyList<string>? conflicts = null) =>
        new(
            id,
            name,
            ApplicationLifecycleStatus.Active,
            [new ProfileRecommendation(profile, level, $"{profile.ToString().ToLowerInvariant()}.fixture")],
            new ApplicationRequirements(
                minimum ?? Requirements(),
                recommended ?? Requirements()),
            [new PlatformSupportRule(
                PlatformKind.Windows,
                PlatformSupportStatus.Supported,
                [MachineArchitecture.X64, MachineArchitecture.Arm64])],
            [],
            conflicts ?? []);

    private static CapabilityRequirements Requirements(
        long? minRamMiB = 512,
        long? minStorageMiB = 256,
        bool gpuRequired = false) =>
        new(
            minRamMiB,
            minStorageMiB,
            gpuRequired,
            [MachineArchitecture.X64, MachineArchitecture.Arm64]);

    private static MachineSnapshot CapableMachine(
        long? totalRamMiB = 16_384,
        long? freeStorageMiB = 64_000,
        GpuCapabilityState gpu = GpuCapabilityState.Available) =>
        new(
            new PlatformSnapshot(
                PlatformKind.Windows,
                "Windows 11 Pro",
                "24H2",
                new Version(10, 0),
                "26100",
                null,
                MachineArchitecture.X64,
                MachineArchitecture.X64),
            new CpuSnapshot("Fixture CPU", MachineArchitecture.X64, 8),
            new MemorySnapshot(
                totalRamMiB is null ? null : MiBToBytesUnsigned(totalRamMiB.Value),
                null),
            gpu == GpuCapabilityState.Available
                ? [new GpuSnapshot("Fixture GPU", "Fixture Vendor")]
                : [],
            [new StorageSnapshot(
                "C:\\",
                StorageKind.Fixed,
                512L * 1024 * 1024 * 1024,
                freeStorageMiB is null ? null : MiBToBytes(freeStorageMiB.Value),
                true)],
            new PackageManagerSnapshot(
                PackageManagerKind.WinGet,
                CapabilityState.Available,
                new Version(1, 12)),
            new CapabilitySnapshot(gpu),
            [],
            DateTimeOffset.UnixEpoch);

    private static DetectedApplicationState Missing(string applicationId) =>
        new(
            applicationId,
            SoftwarePresenceState.Missing,
            null,
            [],
            []);

    private static DetectedApplicationState Unknown(string applicationId) =>
        new(
            applicationId,
            SoftwarePresenceState.Unknown,
            null,
            [],
            [new SoftwareStateDiagnostic("fixture.unknown", "Fixture inventory is incomplete.")]);

    private static DetectedApplicationState Installed(string applicationId, string version) =>
        new(
            applicationId,
            SoftwarePresenceState.Installed,
            version,
            [],
            []);

    private static long MiBToBytes(long value) => value * 1024L * 1024L;

    private static ulong MiBToBytesUnsigned(long value) =>
        checked((ulong)value * 1024UL * 1024UL);
}
