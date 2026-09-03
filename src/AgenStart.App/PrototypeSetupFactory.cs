using AgenStart.Application.GuidedSetup;
using AgenStart.Application.Installation;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.PackageManagement;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.App;

internal static class PrototypeSetupFactory
{
    public static MainWindowViewModel CreateViewModel()
    {
        var candidates = BuildCandidates();
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "visual-studio-code" };
        var verifier = new PrototypeInstallationVerifier(installed);
        var provider = new PrototypePackageProvider(installed);
        var session = new GuidedSetupSession(
            BuildMachine(),
            BuildSoftwareState(candidates, installed),
            candidates,
            new RecommendationEngine(),
            new InstallationOrchestrator([provider], verifier));
        return new MainWindowViewModel(session);
    }

    private static IReadOnlyList<GuidedApplicationCandidate> BuildCandidates() =>
    [
        Candidate("git", "Git", "Git.Git", 512, 500, false,
            [Rule(UserProfile.Development, RecommendationLevel.Essential, "development.source-control"), Rule(UserProfile.Training, RecommendationLevel.Recommended, "training.source-control")]),
        Candidate("visual-studio-code", "Visual Studio Code", "Microsoft.VisualStudioCode", 2048, 1000, false,
            [Rule(UserProfile.Development, RecommendationLevel.Recommended, "development.code-editor"), Rule(UserProfile.Training, RecommendationLevel.Recommended, "training.code-editor")]),
        Candidate("docker-desktop", "Docker Desktop", "Docker.DockerDesktop", 32768, 8192, false,
            [Rule(UserProfile.Development, RecommendationLevel.Optional, "development.containers")]),
        Candidate("firefox", "Mozilla Firefox", "Mozilla.Firefox", 2048, 500, false,
            [Rule(UserProfile.Personal, RecommendationLevel.Recommended, "personal.web-browser"), Rule(UserProfile.Development, RecommendationLevel.Optional, "development.browser-testing")]),
        Candidate("vlc", "VLC media player", "VideoLAN.VLC", 1024, 500, false,
            [Rule(UserProfile.Personal, RecommendationLevel.Recommended, "personal.media-player"), Rule(UserProfile.Creation, RecommendationLevel.Optional, "creation.media-review"), Rule(UserProfile.Training, RecommendationLevel.Optional, "training.media-playback")]),
        Candidate("7zip", "7-Zip", "7zip.7zip", 256, 200, false,
            [Rule(UserProfile.Personal, RecommendationLevel.Optional, "personal.archive-tool"), Rule(UserProfile.Development, RecommendationLevel.Recommended, "development.archive-tool"), Rule(UserProfile.Business, RecommendationLevel.Recommended, "business.archive-tool")]),
        Candidate("obs-studio", "OBS Studio", "OBSProject.OBSStudio", 4096, 1000, true,
            [Rule(UserProfile.Creation, RecommendationLevel.Recommended, "creation.screen-recording"), Rule(UserProfile.Training, RecommendationLevel.Recommended, "training.screen-recording")]),
        Candidate("powertoys", "Microsoft PowerToys", "Microsoft.PowerToys", 2048, 1000, false,
            [Rule(UserProfile.Development, RecommendationLevel.Recommended, "development.windows-productivity"), Rule(UserProfile.Business, RecommendationLevel.Optional, "business.windows-productivity")])
    ];

    private static GuidedApplicationCandidate Candidate(string id, string name, string packageId, long minRamMiB, long minStorageMiB, bool gpuRequired, IReadOnlyList<ProfileRecommendation> recommendations) =>
        new(
            new ApplicationDefinition(
                id, name, ApplicationLifecycleStatus.Active, recommendations,
                new ApplicationRequirements(
                    new CapabilityRequirements(minRamMiB, minStorageMiB, gpuRequired, [MachineArchitecture.X64]),
                    new CapabilityRequirements(Math.Max(minRamMiB, 4096), Math.Max(minStorageMiB, 1024), gpuRequired, [MachineArchitecture.X64])),
                [new PlatformSupportRule(PlatformKind.Windows, PlatformSupportStatus.Supported, [MachineArchitecture.X64])],
                [], []),
            new ProviderPackageReference(PackageProviderIds.WinGet, packageId, "winget"));

    private static ProfileRecommendation Rule(UserProfile profile, RecommendationLevel level, string reasonKey) => new(profile, level, reasonKey);

    private static MachineSnapshot BuildMachine() =>
        new(
            new PlatformSnapshot(PlatformKind.Windows, "Windows 11 Pro", "24H2", new Version(10, 0, 26100), "26100", "0", MachineArchitecture.X64, MachineArchitecture.X64),
            new CpuSnapshot("Intel Core i7 — prototype", MachineArchitecture.X64, 12),
            new MemorySnapshot(16UL * 1024 * 1024 * 1024, 9UL * 1024 * 1024 * 1024),
            [new GpuSnapshot("Dedicated GPU detected", "Prototype")],
            [new StorageSnapshot("C:\\", StorageKind.Fixed, 512L * 1024 * 1024 * 1024, 126L * 1024 * 1024 * 1024, true)],
            new PackageManagerSnapshot(PackageManagerKind.WinGet, CapabilityState.Available, new Version(1, 12, 0)),
            new CapabilitySnapshot(GpuCapabilityState.Available),
            [], DateTimeOffset.UtcNow);

    private static SoftwareDetectionResult BuildSoftwareState(IReadOnlyList<GuidedApplicationCandidate> candidates, IReadOnlySet<string> installed) =>
        new(candidates.Select(candidate => new DetectedApplicationState(
            candidate.Definition.Id,
            installed.Contains(candidate.Definition.Id) ? SoftwarePresenceState.Installed : SoftwarePresenceState.Missing,
            installed.Contains(candidate.Definition.Id) ? "prototype-installed" : null,
            [], [])).ToArray(), 0);

    private sealed class PrototypeInstallationVerifier(ISet<string> installed) : IInstallationVerifier
    {
        public Task<InstallationVerificationResult> VerifyAsync(string applicationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(installed.Contains(applicationId)
                ? new InstallationVerificationResult(InstallationVerificationStatus.Verified, "prototype-1.0", Message: $"{applicationId} is present in the prototype inventory.")
                : new InstallationVerificationResult(InstallationVerificationStatus.NotInstalled, DiagnosticCode: "prototype.not-installed"));
        }
    }

    private sealed class PrototypePackageProvider(ISet<string> installed) : IPackageProvider
    {
        public string ProviderId => PackageProviderIds.WinGet;

        public Task<PackageProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PackageProviderAvailability(PackageProviderAvailabilityStatus.Available, "prototype"));

        public Task<PackageResolutionResult> ResolveAsync(ProviderPackageReference package, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PackageResolutionResult(PackageResolutionStatus.Resolved, package));

        public async Task<PackageOperationResult> InstallAsync(PackageInstallRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(450, cancellationToken).ConfigureAwait(false);
            installed.Add(request.ApplicationId);
            return new PackageOperationResult(PackageOperationStatus.Succeeded, request.ApplicationId, request.Package, Message: "Prototype installation completed.");
        }
    }
}
