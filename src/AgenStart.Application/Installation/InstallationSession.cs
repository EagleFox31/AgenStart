using AgenStart.PackageManagement;

namespace AgenStart.Application.Installation;

public sealed class InstallationSession : IDisposable
{
    private readonly List<InstallationQueueItem> _items;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    internal InstallationSession(
        IReadOnlyList<InstallationSelection> selections,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(selections);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        SessionId = Guid.NewGuid();
        CreatedAtUtc = _timeProvider.GetUtcNow();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _items = new List<InstallationQueueItem>(selections.Count);

        for (var index = 0; index < selections.Count; index++)
        {
            var selection = selections[index] ?? throw new ArgumentException(
                "Installation selections cannot contain null entries.",
                nameof(selections));

            if (string.IsNullOrWhiteSpace(selection.ApplicationId))
            {
                throw new ArgumentException(
                    "Installation selections require a canonical application id.",
                    nameof(selections));
            }

            ArgumentNullException.ThrowIfNull(selection.Package);

            var applicationId = selection.ApplicationId.Trim();
            if (!seen.Add(applicationId))
            {
                throw new InvalidOperationException(
                    $"Application {applicationId} appears more than once in the installation selection.");
            }

            _items.Add(new InstallationQueueItem(
                index + 1,
                selection with { ApplicationId = applicationId },
                selection.Approved
                    ? InstallationQueueItemState.Queued
                    : InstallationQueueItemState.Skipped,
                selection.Approved
                    ? InstallationItemActivity.Waiting
                    : InstallationItemActivity.Skipped,
                selection.Approved ? null : "selection.not-approved",
                selection.Approved
                    ? null
                    : $"{applicationId} was not approved by the user and will not be installed."));
        }
    }

    public Guid SessionId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public InstallationSessionState State { get; private set; } = InstallationSessionState.Ready;

    public IReadOnlyList<InstallationItemSnapshot> Items =>
        _items.Select(static item => item.Snapshot()).ToArray();

    public event EventHandler<InstallationProgressEvent>? ProgressChanged;

    public void Cancel()
    {
        ThrowIfDisposed();

        if (State is InstallationSessionState.Completed or InstallationSessionState.Cancelled)
        {
            return;
        }

        _cancellation.Cancel();
        if (State == InstallationSessionState.Running)
        {
            State = InstallationSessionState.Cancelling;
            Publish(
                null,
                "session.cancellation-requested",
                "Installation session cancellation was requested.");
        }
    }

    public InstallationReport CreateReport()
    {
        ThrowIfDisposed();
        return new InstallationReport(
            SessionId,
            State,
            CreatedAtUtc,
            StartedAtUtc,
            CompletedAtUtc,
            Items);
    }

    internal CancellationToken CancellationToken => _cancellation.Token;
    internal IReadOnlyList<InstallationQueueItem> MutableItems => _items;

    internal void BeginRun()
    {
        ThrowIfDisposed();

        if (State is InstallationSessionState.Running or InstallationSessionState.Cancelling)
        {
            throw new InvalidOperationException("The installation session is already running.");
        }

        if (State == InstallationSessionState.Cancelled)
        {
            throw new InvalidOperationException("A cancelled installation session cannot be executed again.");
        }

        State = InstallationSessionState.Running;
        StartedAtUtc ??= _timeProvider.GetUtcNow();
        CompletedAtUtc = null;
        Publish(null, "session.started", "Installation session started.");
    }

    internal void CompleteRun(bool cancelled)
    {
        State = cancelled
            ? InstallationSessionState.Cancelled
            : InstallationSessionState.Completed;
        CompletedAtUtc = _timeProvider.GetUtcNow();
        Publish(
            null,
            cancelled ? "session.cancelled" : "session.completed",
            cancelled
                ? "Installation session was cancelled."
                : "Installation session completed.");
    }

    internal void PrepareRetry(string applicationId)
    {
        ThrowIfDisposed();

        if (State != InstallationSessionState.Completed)
        {
            throw new InvalidOperationException("Retries are only allowed after the installation session has completed.");
        }

        var item = _items.SingleOrDefault(candidate =>
            string.Equals(candidate.Selection.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            throw new KeyNotFoundException($"Application {applicationId} does not exist in this installation session.");
        }

        if (item.State != InstallationQueueItemState.Failed || !item.CanRetry)
        {
            throw new InvalidOperationException($"Application {applicationId} is not eligible for retry.");
        }

        item.State = InstallationQueueItemState.Queued;
        item.Activity = InstallationItemActivity.Waiting;
        item.DiagnosticCode = null;
        item.Message = null;
        item.CanRetry = false;
        item.RequiresReboot = false;
        item.CompletedAtUtc = null;
        Publish(item, "item.retry-queued", $"{applicationId} was queued for retry.");
    }

    internal void MarkQueuedItemsCancelled(string code, string message)
    {
        foreach (var item in _items.Where(static item => item.State == InstallationQueueItemState.Queued))
        {
            item.State = InstallationQueueItemState.Cancelled;
            item.Activity = InstallationItemActivity.Cancelled;
            item.CanRetry = false;
            item.DiagnosticCode = code;
            item.Message = message;
            item.CompletedAtUtc = _timeProvider.GetUtcNow();
            Publish(item, code, message);
        }
    }

    internal void BeginAttempt(InstallationQueueItem item)
    {
        item.AttemptCount++;
        item.StartedAtUtc = _timeProvider.GetUtcNow();
        item.CompletedAtUtc = null;
        item.DiagnosticCode = null;
        item.Message = null;
        item.CanRetry = false;
        item.RequiresReboot = false;
    }

    internal void MarkResolving(InstallationQueueItem item)
    {
        item.Activity = InstallationItemActivity.Resolving;
        Publish(item, "item.resolving", $"Resolving trusted package for {item.Selection.ApplicationId}.");
    }

    internal void MarkDownloading(InstallationQueueItem item)
    {
        item.Activity = InstallationItemActivity.Downloading;
        item.BytesDownloaded = 0;
        item.BytesRequired = null;
        Publish(item, "item.downloading", $"Downloading {item.Selection.ApplicationId} from its trusted provider.");
    }

    internal void UpdateDownloadProgress(
        InstallationQueueItem item,
        PackagePreparationProgress progress)
    {
        item.Activity = InstallationItemActivity.Downloading;
        if (progress.BytesDownloaded is not null)
        {
            item.BytesDownloaded = Math.Max(0, progress.BytesDownloaded.Value);
        }

        if (progress.BytesRequired is not null)
        {
            item.BytesRequired = Math.Max(0, progress.BytesRequired.Value);
        }

        Publish(
            item,
            "item.download-progress",
            progress.Message ?? $"Downloading {item.Selection.ApplicationId}.");
    }

    internal void MarkReady(
        InstallationQueueItem item,
        long? bytesDownloaded = null,
        string? message = null)
    {
        item.Activity = InstallationItemActivity.Ready;
        if (bytesDownloaded is not null)
        {
            item.BytesDownloaded = Math.Max(0, bytesDownloaded.Value);
        }

        Publish(
            item,
            "item.ready",
            message ?? $"{item.Selection.ApplicationId} is ready to install.");
    }

    internal void MarkInstalling(InstallationQueueItem item)
    {
        item.State = InstallationQueueItemState.Running;
        item.Activity = InstallationItemActivity.Installing;
        Publish(item, "item.installing", $"Installing {item.Selection.ApplicationId}.");
    }

    internal void MarkRunning(InstallationQueueItem item) => MarkInstalling(item);

    internal void MarkVerifying(InstallationQueueItem item)
    {
        item.Activity = InstallationItemActivity.Verifying;
        Publish(item, "item.verifying", $"Verifying {item.Selection.ApplicationId} after installation.");
    }

    internal void MarkSucceeded(
        InstallationQueueItem item,
        string? installedVersion,
        PackageOperationStatus? operationStatus,
        bool requiresReboot,
        string? message = null)
    {
        item.State = InstallationQueueItemState.Succeeded;
        item.Activity = InstallationItemActivity.Completed;
        item.LastOperationStatus = operationStatus;
        item.InstalledVersion = installedVersion;
        item.CanRetry = false;
        item.RequiresReboot = requiresReboot;
        item.DiagnosticCode = requiresReboot ? "installation.reboot-required" : null;
        item.Message = message ?? (requiresReboot
            ? $"{item.Selection.ApplicationId} was verified as installed; a reboot is required to complete the provider operation."
            : $"{item.Selection.ApplicationId} was verified as installed.");
        item.CompletedAtUtc = _timeProvider.GetUtcNow();
        Publish(item, "item.succeeded", item.Message);
    }

    internal void MarkFailed(
        InstallationQueueItem item,
        string code,
        string message,
        bool canRetry,
        PackageOperationStatus? operationStatus = null)
    {
        item.State = InstallationQueueItemState.Failed;
        item.Activity = InstallationItemActivity.Failed;
        item.LastOperationStatus = operationStatus;
        item.DiagnosticCode = code;
        item.Message = message;
        item.CanRetry = canRetry;
        item.CompletedAtUtc = _timeProvider.GetUtcNow();
        Publish(item, code, message);
    }

    internal void MarkCancelled(
        InstallationQueueItem item,
        string code,
        string message,
        PackageOperationStatus? operationStatus = null)
    {
        item.State = InstallationQueueItemState.Cancelled;
        item.Activity = InstallationItemActivity.Cancelled;
        item.LastOperationStatus = operationStatus;
        item.DiagnosticCode = code;
        item.Message = message;
        item.CanRetry = false;
        item.CompletedAtUtc = _timeProvider.GetUtcNow();
        Publish(item, code, message);
    }

    internal void Publish(
        InstallationQueueItem? item,
        string code,
        string message) =>
        ProgressChanged?.Invoke(
            this,
            new InstallationProgressEvent(
                SessionId,
                State,
                item?.Snapshot(),
                code,
                message,
                _timeProvider.GetUtcNow()));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Dispose();
    }

    internal sealed class InstallationQueueItem(
        int sequence,
        InstallationSelection selection,
        InstallationQueueItemState state,
        InstallationItemActivity activity,
        string? diagnosticCode,
        string? message)
    {
        public int Sequence { get; } = sequence;
        public InstallationSelection Selection { get; } = selection;
        public InstallationQueueItemState State { get; set; } = state;
        public InstallationItemActivity Activity { get; set; } = activity;
        public int AttemptCount { get; set; }
        public PackageOperationStatus? LastOperationStatus { get; set; }
        public string? DiagnosticCode { get; set; } = diagnosticCode;
        public string? Message { get; set; } = message;
        public string? InstalledVersion { get; set; }
        public bool CanRetry { get; set; }
        public bool RequiresReboot { get; set; }
        public long? BytesDownloaded { get; set; }
        public long? BytesRequired { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }

        public InstallationItemSnapshot Snapshot() =>
            new(
                Sequence,
                Selection.ApplicationId,
                Selection.Package,
                State,
                Activity,
                AttemptCount,
                LastOperationStatus,
                DiagnosticCode,
                Message,
                InstalledVersion,
                CanRetry,
                RequiresReboot,
                BytesDownloaded,
                BytesRequired,
                StartedAtUtc,
                CompletedAtUtc);
    }
}
