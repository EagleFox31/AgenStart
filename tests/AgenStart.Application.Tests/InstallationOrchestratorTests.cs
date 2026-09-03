using AgenStart.Application.Installation;
using AgenStart.PackageManagement;

namespace AgenStart.Application.Tests;

public sealed class InstallationOrchestratorTests
{
    [Fact]
    public async Task RunAsync_ExecutesOnlyApprovedItemsSequentiallyAndReportsSkippedItems()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var provider = new FakePackageProvider
        {
            OnInstall = request =>
            {
                installed.Add(request.ApplicationId);
                return Success(request);
            }
        };
        var verifier = new FakeVerifier(applicationId =>
            Task.FromResult(installed.Contains(applicationId)
                ? Verified("1.0.0")
                : Missing()));
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        using var session = orchestrator.CreateSession(
        [
            Selection("git", approved: true),
            Selection("vlc", approved: false),
            Selection("7zip", approved: true)
        ]);

        var report = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);

        Assert.Equal(["git", "7zip"], provider.InstallOrder);
        Assert.Equal(2, report.SucceededCount);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(InstallationSessionState.Completed, report.State);
        Assert.Equal(
            InstallationQueueItemState.Skipped,
            Assert.Single(report.Items, item => item.ApplicationId == "vlc").State);
    }

    [Fact]
    public async Task RetryAsync_RechecksInventoryAndDoesNotRepeatACompletedInstall()
    {
        var provider = new FakePackageProvider();
        var verificationResults = new Queue<InstallationVerificationResult>(
        [
            Missing(),
            Missing(),
            Verified("2.51.0")
        ]);
        var verifier = new FakeVerifier(_ => Task.FromResult(verificationResults.Dequeue()));
        var orchestrator = new InstallationOrchestrator([provider], verifier);
        using var session = orchestrator.CreateSession([Selection("git", approved: true)]);

        var firstReport = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);
        var failed = Assert.Single(firstReport.Items);

        Assert.Equal(InstallationQueueItemState.Failed, failed.State);
        Assert.True(failed.CanRetry);
        Assert.Equal(1, provider.InstallCount);

        var retryReport = await orchestrator.RetryAsync(
            session,
            "git",
            TestContext.Current.CancellationToken);
        var retried = Assert.Single(retryReport.Items);

        Assert.Equal(InstallationQueueItemState.Succeeded, retried.State);
        Assert.Equal(PackageOperationStatus.AlreadyInstalled, retried.LastOperationStatus);
        Assert.Equal("2.51.0", retried.InstalledVersion);
        Assert.Equal(1, provider.InstallCount);
        Assert.Equal(2, retried.AttemptCount);
    }

    [Fact]
    public async Task RunAsync_NormalizesRetryableProviderFailure()
    {
        var provider = new FakePackageProvider
        {
            OnInstall = request => new PackageOperationResult(
                PackageOperationStatus.NetworkFailure,
                request.ApplicationId,
                request.Package,
                DiagnosticCode: "winget.network-failure",
                Message: "Network unavailable.")
        };
        var orchestrator = new InstallationOrchestrator(
            [provider],
            new FakeVerifier(_ => Task.FromResult(Missing())));
        using var session = orchestrator.CreateSession([Selection("git", approved: true)]);

        var report = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);
        var item = Assert.Single(report.Items);

        Assert.Equal(InstallationQueueItemState.Failed, item.State);
        Assert.Equal(PackageOperationStatus.NetworkFailure, item.LastOperationStatus);
        Assert.Equal("winget.network-failure", item.DiagnosticCode);
        Assert.True(item.CanRetry);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsCurrentAndRemainingQueuedItems()
    {
        InstallationSession? session = null;
        var provider = new FakePackageProvider
        {
            OnInstall = request =>
            {
                session!.Cancel();
                return new PackageOperationResult(
                    PackageOperationStatus.Cancelled,
                    request.ApplicationId,
                    request.Package,
                    DiagnosticCode: "winget.cancelled",
                    Message: "Cancelled.");
            }
        };
        var orchestrator = new InstallationOrchestrator(
            [provider],
            new FakeVerifier(_ => Task.FromResult(Missing())));
        using var createdSession = orchestrator.CreateSession(
        [
            Selection("git", approved: true),
            Selection("7zip", approved: true)
        ]);
        session = createdSession;

        var report = await orchestrator.RunAsync(
            createdSession,
            TestContext.Current.CancellationToken);

        Assert.Equal(InstallationSessionState.Cancelled, report.State);
        Assert.Equal(2, report.CancelledCount);
        Assert.Equal(1, provider.InstallCount);
        Assert.All(
            report.Items,
            item => Assert.Equal(InstallationQueueItemState.Cancelled, item.State));
    }

    [Fact]
    public async Task RunAsync_DoesNotClaimSuccessWhenPostInstallVerificationIsUnknown()
    {
        var results = new Queue<InstallationVerificationResult>(
        [
            Missing(),
            new InstallationVerificationResult(
                InstallationVerificationStatus.Unknown,
                DiagnosticCode: "verification.inventory-unknown",
                Message: "Inventory incomplete.")
        ]);
        var provider = new FakePackageProvider();
        var orchestrator = new InstallationOrchestrator(
            [provider],
            new FakeVerifier(_ => Task.FromResult(results.Dequeue())));
        using var session = orchestrator.CreateSession([Selection("git", approved: true)]);

        var report = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);
        var item = Assert.Single(report.Items);

        Assert.Equal(InstallationQueueItemState.Failed, item.State);
        Assert.Equal("verification.inventory-unknown", item.DiagnosticCode);
        Assert.False(item.CanRetry);
        Assert.Equal(1, provider.InstallCount);
    }

    [Fact]
    public async Task RunAsync_VerifiedRebootRequiredOperationIsReportedAsSuccess()
    {
        var results = new Queue<InstallationVerificationResult>([Missing(), Verified("5.0")]);
        var provider = new FakePackageProvider
        {
            OnInstall = request => new PackageOperationResult(
                PackageOperationStatus.RebootRequired,
                request.ApplicationId,
                request.Package,
                Message: "Reboot required.")
        };
        var orchestrator = new InstallationOrchestrator(
            [provider],
            new FakeVerifier(_ => Task.FromResult(results.Dequeue())));
        using var session = orchestrator.CreateSession([Selection("powertoys", approved: true)]);

        var report = await orchestrator.RunAsync(
            session,
            TestContext.Current.CancellationToken);
        var item = Assert.Single(report.Items);

        Assert.Equal(InstallationQueueItemState.Succeeded, item.State);
        Assert.True(item.RequiresReboot);
        Assert.Equal(PackageOperationStatus.RebootRequired, item.LastOperationStatus);
        Assert.Equal("5.0", item.InstalledVersion);
    }

    private static InstallationSelection Selection(string applicationId, bool approved) =>
        new(
            applicationId,
            new ProviderPackageReference(
                PackageProviderIds.WinGet,
                $"Example.{applicationId}",
                "winget"),
            approved);

    private static InstallationVerificationResult Missing() =>
        new(InstallationVerificationStatus.NotInstalled);

    private static InstallationVerificationResult Verified(string version) =>
        new(InstallationVerificationStatus.Verified, version);

    private static PackageOperationResult Success(PackageInstallRequest request) =>
        new(
            PackageOperationStatus.Succeeded,
            request.ApplicationId,
            request.Package);

    private sealed class FakeVerifier(
        Func<string, Task<InstallationVerificationResult>> verify) : IInstallationVerifier
    {
        public Task<InstallationVerificationResult> VerifyAsync(
            string applicationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return verify(applicationId);
        }
    }

    private sealed class FakePackageProvider : IPackageProvider
    {
        public string ProviderId => PackageProviderIds.WinGet;
        public Func<PackageInstallRequest, PackageOperationResult>? OnInstall { get; init; }
        public List<string> InstallOrder { get; } = [];
        public int InstallCount => InstallOrder.Count;

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

        public Task<PackageOperationResult> InstallAsync(
            PackageInstallRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallOrder.Add(request.ApplicationId);
            return Task.FromResult(OnInstall?.Invoke(request) ?? Success(request));
        }
    }
}
