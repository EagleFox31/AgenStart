using System.Collections.Concurrent;
using AgenStart.PackageManagement;

namespace AgenStart.Application.Installation;

public sealed class InstallationOrchestrator
{
    private readonly IReadOnlyDictionary<string, IPackageProvider> _providers;
    private readonly IInstallationVerifier _verifier;
    private readonly TimeProvider _timeProvider;
    private readonly int _preparationConcurrency;
    private readonly ConcurrentDictionary<string, PackagePreparationResult> _preparations =
        new(StringComparer.OrdinalIgnoreCase);

    public InstallationOrchestrator(
        IEnumerable<IPackageProvider> providers,
        IInstallationVerifier verifier,
        TimeProvider? timeProvider = null,
        int preparationConcurrency = 3)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (preparationConcurrency is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preparationConcurrency),
                "Package preparation concurrency must be between 1 and 3.");
        }

        _preparationConcurrency = preparationConcurrency;

        var providerMap = new Dictionary<string, IPackageProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (string.IsNullOrWhiteSpace(provider.ProviderId))
            {
                throw new ArgumentException("Package providers must expose a provider id.", nameof(providers));
            }

            if (!providerMap.TryAdd(provider.ProviderId.Trim(), provider))
            {
                throw new ArgumentException(
                    $"Package provider {provider.ProviderId} was registered more than once.",
                    nameof(providers));
            }
        }

        _providers = providerMap;
    }

    public int PreparationConcurrency => _preparationConcurrency;

    public InstallationSession CreateSession(IReadOnlyList<InstallationSelection> selections) =>
        new(selections, _timeProvider);

    public async Task<InstallationReport> RunAsync(
        InstallationSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.BeginRun();

        using var pipelineCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            session.CancellationToken,
            cancellationToken);
        var token = pipelineCancellation.Token;
        var cancelled = false;
        var candidates = new List<InstallationSession.InstallationQueueItem>();

        foreach (var item in session.MutableItems)
        {
            if (item.State != InstallationQueueItemState.Queued)
            {
                continue;
            }

            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            session.BeginAttempt(item);

            try
            {
                var preVerification = await _verifier
                    .VerifyAsync(item.Selection.ApplicationId, token)
                    .ConfigureAwait(false);

                if (preVerification.Status == InstallationVerificationStatus.Verified)
                {
                    await ReleasePreparationForItemAsync(session, item).ConfigureAwait(false);
                    session.MarkSucceeded(
                        item,
                        preVerification.InstalledVersion,
                        PackageOperationStatus.AlreadyInstalled,
                        requiresReboot: false,
                        $"{item.Selection.ApplicationId} is already installed; provider execution was skipped.");
                    continue;
                }

                candidates.Add(item);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (Exception exception)
            {
                session.MarkFailed(
                    item,
                    "installation.preverification-failed",
                    $"Installed-state verification failed unexpectedly: {exception.GetType().Name}.",
                    canRetry: false,
                    PackageOperationStatus.Failed);
            }
        }

        var preparationTasks = new Dictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);
        if (!cancelled && candidates.Count > 0)
        {
            using var preparationGate = new SemaphoreSlim(_preparationConcurrency, _preparationConcurrency);
            var availabilityTasks = new ConcurrentDictionary<string, Task<PackageProviderAvailability>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var item in candidates)
            {
                preparationTasks[item.Selection.ApplicationId] = PrepareItemAsync(
                    session,
                    item,
                    preparationGate,
                    availabilityTasks,
                    token);
            }

            foreach (var item in candidates)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                var preparationCancelled = await preparationTasks[item.Selection.ApplicationId]
                    .ConfigureAwait(false);
                if (preparationCancelled)
                {
                    cancelled = true;
                    pipelineCancellation.Cancel();
                    break;
                }

                if (item.State != InstallationQueueItemState.Queued ||
                    item.Activity != InstallationItemActivity.Ready)
                {
                    continue;
                }

                cancelled = await ExecuteReadyItemAsync(session, item, token).ConfigureAwait(false);
                if (cancelled)
                {
                    pipelineCancellation.Cancel();
                    break;
                }
            }

            if (cancelled)
            {
                await ObservePreparationTasksAsync(preparationTasks.Values).ConfigureAwait(false);
            }
        }

        if (cancelled || token.IsCancellationRequested)
        {
            session.MarkQueuedItemsCancelled(
                "installation.cancelled-before-start",
                "The item was cancelled before execution started.");
            session.CompleteRun(cancelled: true);
        }
        else
        {
            session.CompleteRun(cancelled: false);
        }

        return session.CreateReport();
    }

    public async Task<InstallationReport> RetryAsync(
        InstallationSession session,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("An application id is required.", nameof(applicationId));
        }

        session.PrepareRetry(applicationId.Trim());
        return await RunAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PrepareItemAsync(
        InstallationSession session,
        InstallationSession.InstallationQueueItem item,
        SemaphoreSlim preparationGate,
        ConcurrentDictionary<string, Task<PackageProviderAvailability>> availabilityTasks,
        CancellationToken cancellationToken)
    {
        try
        {
            await preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        try
        {
            if (TryGetPreparation(session, item, out var cachedPreparation) && cachedPreparation.IsReady)
            {
                session.MarkReady(
                    item,
                    cachedPreparation.BytesDownloaded,
                    $"{item.Selection.ApplicationId} is already prepared and ready to install.");
                return false;
            }

            session.MarkResolving(item);

            if (!_providers.TryGetValue(item.Selection.Package.ProviderId, out var provider))
            {
                session.MarkFailed(
                    item,
                    "installation.provider-not-registered",
                    $"Provider {item.Selection.Package.ProviderId} is not registered for this session.",
                    canRetry: false,
                    PackageOperationStatus.ProviderUnavailable);
                return false;
            }

            var availabilityTask = availabilityTasks.GetOrAdd(
                provider.ProviderId,
                _ => provider.CheckAvailabilityAsync(cancellationToken));
            var availability = await availabilityTask.ConfigureAwait(false);

            if (!availability.IsAvailable)
            {
                session.MarkFailed(
                    item,
                    availability.DiagnosticCode ?? "installation.provider-unavailable",
                    availability.Message ?? $"Provider {provider.ProviderId} is unavailable.",
                    CanRetryAvailability(availability.Status),
                    PackageOperationStatus.ProviderUnavailable);
                return false;
            }

            var resolution = await provider
                .ResolveAsync(item.Selection.Package, cancellationToken)
                .ConfigureAwait(false);

            if (!resolution.IsResolved)
            {
                if (resolution.Status == PackageResolutionStatus.Cancelled)
                {
                    session.MarkCancelled(
                        item,
                        resolution.DiagnosticCode ?? "installation.resolution-cancelled",
                        resolution.Message ?? "Package resolution was cancelled.",
                        PackageOperationStatus.Cancelled);
                    return true;
                }

                session.MarkFailed(
                    item,
                    resolution.DiagnosticCode ?? "installation.package-resolution-failed",
                    resolution.Message ?? $"Package {item.Selection.Package.PackageId} could not be resolved.",
                    CanRetryResolution(resolution.Status),
                    MapResolutionStatus(resolution.Status));
                return false;
            }

            if (provider is not IPreparablePackageProvider preparable ||
                !preparable.CanPrepare(item.Selection.Package))
            {
                session.MarkReady(
                    item,
                    message: $"{item.Selection.ApplicationId} is resolved and ready for sequential installation.");
                return false;
            }

            session.MarkDownloading(item);
            var request = CreateInstallRequest(item.Selection);
            var progress = new CallbackProgress<PackagePreparationProgress>(value =>
                session.UpdateDownloadProgress(item, value));
            var preparation = await preparable
                .PrepareAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);

            if (preparation.Status == PackagePreparationStatus.Cancelled)
            {
                session.MarkCancelled(
                    item,
                    preparation.DiagnosticCode ?? "installation.download-cancelled",
                    preparation.Message ?? "Package preparation was cancelled.",
                    PackageOperationStatus.Cancelled);
                return true;
            }

            if (preparation.Status == PackagePreparationStatus.Unsupported)
            {
                session.MarkReady(
                    item,
                    message: preparation.Message ??
                        $"{item.Selection.ApplicationId} will use the provider's normal sequential install path.");
                return false;
            }

            if (!preparation.IsReady)
            {
                session.MarkFailed(
                    item,
                    preparation.DiagnosticCode ?? "installation.package-preparation-failed",
                    preparation.Message ?? $"Package {item.Selection.Package.PackageId} could not be prepared.",
                    CanRetryPreparation(preparation.Status),
                    MapPreparationStatus(preparation.Status));
                return false;
            }

            _preparations[PreparationKey(session, item.Selection.ApplicationId)] = preparation;
            session.MarkReady(
                item,
                preparation.BytesDownloaded,
                preparation.Message ?? $"{item.Selection.ApplicationId} is downloaded and ready to install.");
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception exception)
        {
            session.MarkFailed(
                item,
                "installation.unhandled-preparation-error",
                $"Package preparation failed unexpectedly: {exception.GetType().Name}.",
                canRetry: false,
                PackageOperationStatus.Failed);
            return false;
        }
        finally
        {
            preparationGate.Release();
        }
    }

    private async Task<bool> ExecuteReadyItemAsync(
        InstallationSession session,
        InstallationSession.InstallationQueueItem item,
        CancellationToken cancellationToken)
    {
        session.MarkInstalling(item);

        try
        {
            if (!_providers.TryGetValue(item.Selection.Package.ProviderId, out var provider))
            {
                session.MarkFailed(
                    item,
                    "installation.provider-not-registered",
                    $"Provider {item.Selection.Package.ProviderId} is not registered for this session.",
                    canRetry: false,
                    PackageOperationStatus.ProviderUnavailable);
                return false;
            }

            var request = CreateInstallRequest(item.Selection);
            PackageOperationResult operation;

            if (provider is IPreparablePackageProvider preparable &&
                TryGetPreparation(session, item, out var preparation) &&
                preparation.IsReady)
            {
                operation = await preparable
                    .InstallPreparedAsync(request, preparation, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                operation = await provider
                    .InstallAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (operation.Status is PackageOperationStatus.Cancelled or PackageOperationStatus.CancelledByUser)
            {
                session.MarkCancelled(
                    item,
                    operation.DiagnosticCode ?? "installation.cancelled",
                    operation.Message ?? "Package installation was cancelled.",
                    operation.Status);
                return true;
            }

            if (!IsVerifiableCompletion(operation.Status))
            {
                session.MarkFailed(
                    item,
                    operation.DiagnosticCode ?? "installation.provider-failed",
                    operation.Message ?? $"Provider installation failed with status {operation.Status}.",
                    CanRetryOperation(operation.Status),
                    operation.Status);
                return false;
            }

            session.MarkVerifying(item);
            var verification = await _verifier
                .VerifyAsync(item.Selection.ApplicationId, cancellationToken)
                .ConfigureAwait(false);

            if (verification.Status == InstallationVerificationStatus.Verified)
            {
                session.MarkSucceeded(
                    item,
                    verification.InstalledVersion,
                    operation.Status,
                    operation.Status == PackageOperationStatus.RebootRequired,
                    verification.Message);
                await ReleasePreparationForItemAsync(session, item).ConfigureAwait(false);
                return false;
            }

            if (verification.Status == InstallationVerificationStatus.NotInstalled)
            {
                session.MarkFailed(
                    item,
                    verification.DiagnosticCode ?? "installation.verification-not-installed",
                    verification.Message ?? "The provider completed, but the application was not detected after installation.",
                    canRetry: true,
                    operation.Status);
                return false;
            }

            session.MarkFailed(
                item,
                verification.DiagnosticCode ?? "installation.verification-unknown",
                verification.Message ?? "Installation completed, but installed state could not be verified safely.",
                canRetry: false,
                operation.Status);
            await ReleasePreparationForItemAsync(session, item).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.MarkCancelled(
                item,
                "installation.cancelled",
                "Package installation was cancelled.",
                PackageOperationStatus.Cancelled);
            return true;
        }
        catch (Exception exception)
        {
            session.MarkFailed(
                item,
                "installation.unhandled-provider-error",
                $"The installation operation failed unexpectedly: {exception.GetType().Name}.",
                canRetry: false,
                PackageOperationStatus.Failed);
            return false;
        }
    }

    private async Task ReleasePreparationForItemAsync(
        InstallationSession session,
        InstallationSession.InstallationQueueItem item)
    {
        var key = PreparationKey(session, item.Selection.ApplicationId);
        if (!_preparations.TryRemove(key, out var preparation) ||
            !_providers.TryGetValue(item.Selection.Package.ProviderId, out var provider) ||
            provider is not IPreparablePackageProvider preparable)
        {
            return;
        }

        try
        {
            await preparable.ReleasePreparationAsync(preparation, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // Cache cleanup is best-effort and must never turn a verified installation into a failure.
        }
    }

    private bool TryGetPreparation(
        InstallationSession session,
        InstallationSession.InstallationQueueItem item,
        out PackagePreparationResult preparation) =>
        _preparations.TryGetValue(
            PreparationKey(session, item.Selection.ApplicationId),
            out preparation!);

    private static string PreparationKey(InstallationSession session, string applicationId) =>
        $"{session.SessionId:N}:{applicationId.Trim().ToLowerInvariant()}";

    private static PackageInstallRequest CreateInstallRequest(InstallationSelection selection) =>
        new(
            selection.ApplicationId,
            selection.Package,
            selection.Silent,
            selection.AcceptPackageAgreements,
            selection.AcceptSourceAgreements);

    private static async Task ObservePreparationTasksAsync(IEnumerable<Task<bool>> tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is represented in the installation session state.
        }
    }

    private static bool IsVerifiableCompletion(PackageOperationStatus status) =>
        status is PackageOperationStatus.Succeeded
            or PackageOperationStatus.AlreadyInstalled
            or PackageOperationStatus.RebootRequired;

    private static bool CanRetryAvailability(PackageProviderAvailabilityStatus status) =>
        status is PackageProviderAvailabilityStatus.NotInstalled
            or PackageProviderAvailabilityStatus.Unhealthy;

    private static bool CanRetryResolution(PackageResolutionStatus status) =>
        status is PackageResolutionStatus.SourceUnavailable
            or PackageResolutionStatus.TimedOut
            or PackageResolutionStatus.ProviderUnavailable
            or PackageResolutionStatus.Failed;

    private static bool CanRetryPreparation(PackagePreparationStatus status) =>
        status is PackagePreparationStatus.NetworkFailure
            or PackagePreparationStatus.SourceUnavailable
            or PackagePreparationStatus.TimedOut
            or PackagePreparationStatus.ProviderUnavailable
            or PackagePreparationStatus.Failed;

    private static bool CanRetryOperation(PackageOperationStatus status) =>
        status is PackageOperationStatus.NetworkFailure
            or PackageOperationStatus.SourceUnavailable
            or PackageOperationStatus.TimedOut
            or PackageOperationStatus.ProviderUnavailable
            or PackageOperationStatus.Failed;

    private static PackageOperationStatus? MapResolutionStatus(PackageResolutionStatus status) =>
        status switch
        {
            PackageResolutionStatus.NotFound => PackageOperationStatus.NotFound,
            PackageResolutionStatus.Ambiguous => PackageOperationStatus.Ambiguous,
            PackageResolutionStatus.NoApplicableInstaller => PackageOperationStatus.NoApplicableInstaller,
            PackageResolutionStatus.SourceUnavailable => PackageOperationStatus.SourceUnavailable,
            PackageResolutionStatus.AgreementRequired => PackageOperationStatus.AgreementRequired,
            PackageResolutionStatus.BlockedByPolicy => PackageOperationStatus.BlockedByPolicy,
            PackageResolutionStatus.IntegrityFailure => PackageOperationStatus.IntegrityFailure,
            PackageResolutionStatus.Cancelled => PackageOperationStatus.Cancelled,
            PackageResolutionStatus.TimedOut => PackageOperationStatus.TimedOut,
            PackageResolutionStatus.ProviderUnavailable => PackageOperationStatus.ProviderUnavailable,
            PackageResolutionStatus.Failed => PackageOperationStatus.Failed,
            _ => null
        };

    private static PackageOperationStatus? MapPreparationStatus(PackagePreparationStatus status) =>
        status switch
        {
            PackagePreparationStatus.SourceUnavailable => PackageOperationStatus.SourceUnavailable,
            PackagePreparationStatus.AgreementRequired => PackageOperationStatus.AgreementRequired,
            PackagePreparationStatus.BlockedByPolicy => PackageOperationStatus.BlockedByPolicy,
            PackagePreparationStatus.IntegrityFailure => PackageOperationStatus.IntegrityFailure,
            PackagePreparationStatus.NetworkFailure => PackageOperationStatus.NetworkFailure,
            PackagePreparationStatus.Cancelled => PackageOperationStatus.Cancelled,
            PackagePreparationStatus.TimedOut => PackageOperationStatus.TimedOut,
            PackagePreparationStatus.ProviderUnavailable => PackageOperationStatus.ProviderUnavailable,
            PackagePreparationStatus.Failed => PackageOperationStatus.Failed,
            _ => null
        };

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
