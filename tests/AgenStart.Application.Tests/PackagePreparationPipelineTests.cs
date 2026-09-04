using AgenStart.Application.Installation;
using AgenStart.PackageManagement;

namespace AgenStart.Application.Tests;

public sealed class PackagePreparationPipelineTests
{
    [Fact]
    public async Task RunAsync_PreparesPackagesConcurrentlyButInstallsSequentially()
    {
        var provider = new PreparingProvider();
        var verifier = new ProviderBackedVerifier(provider);
        var orchestrator = new InstallationOrchestrator(
            [provider],
            verifier,
            preparationConcurrency: 3);
        using var session = orchestrator.CreateSession(
        [
            Selection("git"),
            Selection("7zip"),
            Selection("visual-studio-code")
        ]);

        var report = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);

        Assert.InRange(provider.MaxPreparationConcurrency, 2, 3);
        Assert.Equal(1, provider.MaxInstallConcurrency);
        Assert.Equal(
            ["git", "7zip", "visual-studio-code"],
            provider.InstallOrder);
        Assert.Equal(3, report.SucceededCount);
        Assert.All(
            report.Items,
            item => Assert.Equal(InstallationItemActivity.Completed, item.Activity));
    }

    [Fact]
    public async Task RunAsync_PreparationFailureDoesNotCorruptOtherQueueItems()
    {
        var provider = new PreparingProvider("7zip");
        var verifier = new ProviderBackedVerifier(provider);
        var orchestrator = new InstallationOrchestrator(
            [provider],
            verifier,
            preparationConcurrency: 3);
        using var session = orchestrator.CreateSession(
        [
            Selection("git"),
            Selection("7zip"),
            Selection("visual-studio-code")
        ]);

        var report = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, report.SucceededCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(["git", "visual-studio-code"], provider.InstallOrder);

        var failed = Assert.Single(report.Items, item => item.ApplicationId == "7zip");
        Assert.Equal(InstallationQueueItemState.Failed, failed.State);
        Assert.Equal(InstallationItemActivity.Failed, failed.Activity);
        Assert.True(failed.CanRetry);
        Assert.Equal(PackageOperationStatus.NetworkFailure, failed.LastOperationStatus);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsActivePreparationsBeforeInstallerExecution()
    {
        var provider = new PreparingProvider(preparationDelay: TimeSpan.FromSeconds(5));
        var verifier = new ProviderBackedVerifier(provider);
        var orchestrator = new InstallationOrchestrator(
            [provider],
            verifier,
            preparationConcurrency: 3);
        using var session = orchestrator.CreateSession(
        [
            Selection("git"),
            Selection("7zip"),
            Selection("visual-studio-code")
        ]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        var report = await orchestrator.RunAsync(session, cancellation.Token);

        Assert.Equal(InstallationSessionState.Cancelled, report.State);
        Assert.Empty(provider.InstallOrder);
        Assert.All(
            report.Items,
            item => Assert.Equal(InstallationQueueItemState.Cancelled, item.State));
    }

    private static InstallationSelection Selection(string applicationId) =>
        new(
            applicationId,
            new ProviderPackageReference(
                PackageProviderIds.WinGet,
                $"Example.{applicationId}",
                "winget"),
            Approved: true);

    private sealed class ProviderBackedVerifier(PreparingProvider provider) : IInstallationVerifier
    {
        public Task<InstallationVerificationResult> VerifyAsync(
            string applicationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(provider.IsInstalled(applicationId)
                ? new InstallationVerificationResult(
                    InstallationVerificationStatus.Verified,
                    "1.0.0")
                : new InstallationVerificationResult(InstallationVerificationStatus.NotInstalled));
        }
    }

    private sealed class PreparingProvider : IPreparablePackageProvider
    {
        private readonly string? _preparationFailureApplicationId;
        private readonly TimeSpan _preparationDelay;
        private readonly HashSet<string> _installed = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();
        private int _activePreparations;
        private int _maxPreparationConcurrency;
        private int _activeInstalls;
        private int _maxInstallConcurrency;

        public PreparingProvider(
            string? preparationFailureApplicationId = null,
            TimeSpan? preparationDelay = null)
        {
            _preparationFailureApplicationId = preparationFailureApplicationId;
            _preparationDelay = preparationDelay ?? TimeSpan.FromMilliseconds(80);
        }

        public string ProviderId => PackageProviderIds.WinGet;
        public List<string> InstallOrder { get; } = [];
        public int MaxPreparationConcurrency => Volatile.Read(ref _maxPreparationConcurrency);
        public int MaxInstallConcurrency => Volatile.Read(ref _maxInstallConcurrency);

        public bool IsInstalled(string applicationId)
        {
            lock (_gate)
            {
                return _installed.Contains(applicationId);
            }
        }

        public bool CanPrepare(ProviderPackageReference package) => true;

        public Task<PackageProviderAvailability> CheckAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PackageProviderAvailability(
                PackageProviderAvailabilityStatus.Available,
                "1.12.0"));
        }

        public Task<PackageResolutionResult> ResolveAsync(
            ProviderPackageReference package,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PackageResolutionResult(
                PackageResolutionStatus.Resolved,
                package));
        }

        public async Task<PackagePreparationResult> PrepareAsync(
            PackageInstallRequest request,
            IProgress<PackagePreparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activePreparations);
            UpdateMax(ref _maxPreparationConcurrency, active);
            try
            {
                progress?.Report(new PackagePreparationProgress(
                    BytesDownloaded: 1024,
                    Message: "Downloading test package."));
                await Task.Delay(_preparationDelay, cancellationToken);

                if (string.Equals(
                        request.ApplicationId,
                        _preparationFailureApplicationId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new PackagePreparationResult(
                        PackagePreparationStatus.NetworkFailure,
                        request.ApplicationId,
                        request.Package,
                        DiagnosticCode: "test.download-failed",
                        Message: "Synthetic network failure.");
                }

                return new PackagePreparationResult(
                    PackagePreparationStatus.Ready,
                    request.ApplicationId,
                    request.Package,
                    PreparationId: $"prepared-{request.ApplicationId}",
                    BytesDownloaded: 4096);
            }
            finally
            {
                Interlocked.Decrement(ref _activePreparations);
            }
        }

        public async Task<PackageOperationResult> InstallPreparedAsync(
            PackageInstallRequest request,
            PackagePreparationResult preparation,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeInstalls);
            UpdateMax(ref _maxInstallConcurrency, active);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
                lock (_gate)
                {
                    InstallOrder.Add(request.ApplicationId);
                    _installed.Add(request.ApplicationId);
                }

                return new PackageOperationResult(
                    PackageOperationStatus.Succeeded,
                    request.ApplicationId,
                    request.Package);
            }
            finally
            {
                Interlocked.Decrement(ref _activeInstalls);
            }
        }

        public Task ReleasePreparationAsync(
            PackagePreparationResult preparation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<PackageOperationResult> InstallAsync(
            PackageInstallRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Prepared test packages must use InstallPreparedAsync.");

        private static void UpdateMax(ref int target, int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (candidate <= current ||
                    Interlocked.CompareExchange(ref target, candidate, current) == current)
                {
                    return;
                }
            }
        }
    }
}
