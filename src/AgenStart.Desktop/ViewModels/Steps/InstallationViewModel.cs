using System.Collections.ObjectModel;
using AgenStart.Application.Installation;
using AgenStart.Catalogue;
using AgenStart.Desktop.Services;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.SoftwareInventory;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class InstallationViewModel : StepViewModelBase, IDisposable
{
    private readonly ISessionState _session;
    private bool _hasReport;
    private readonly IUiDispatcher _dispatcher;
    private readonly IInstalledSoftwareInventoryProvider _softwareInventory;
    private readonly SoftwareStateResolver _softwareStateResolver;

    private InstallationOrchestrator? _installationOrchestrator;
    private InstallationSession? _installationSession;
    private bool _isInstalling;
    private UiText _installationStatus = UiText.Key("Installation.Status.Ready");
    private UiText _currentInstallationName = UiText.Key("Installation.Current.Waiting");

    public InstallationViewModel(
        ISessionState session,
        IUiDispatcher dispatcher,
        IInstalledSoftwareInventoryProvider softwareInventory,
        SoftwareStateResolver softwareStateResolver,
        ILocalizer? text = null)
        : base(text)
    {
        _session = session;
        _dispatcher = dispatcher;
        _softwareInventory = softwareInventory;
        _softwareStateResolver = softwareStateResolver;

        InstallationItems.CollectionChanged += (_, _) => RaiseSummary();
    }

    public event EventHandler? Starting;
    public event EventHandler<InstallationReport>? Completed;

    public ObservableCollection<InstallationRowViewModel> InstallationItems { get; } = [];

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetField(ref _isInstalling, value))
            {
                _session.SetIsInstalling(value);
            }
        }
    }

    public string InstallationStatus => _installationStatus.Resolve(Text);

    public string CurrentInstallationName => _currentInstallationName.Resolve(Text);

    public int InstallationCompletedCount => InstallationItems.Count(row => row.State == InstallationQueueItemState.Succeeded);
    public int InstallationFailedCount => InstallationItems.Count(row => row.State == InstallationQueueItemState.Failed);
    public int InstallationRemainingCount => InstallationItems.Count(row => row.State is InstallationQueueItemState.Queued or InstallationQueueItemState.Running);
    public int InstallationTotalCount => InstallationItems.Count;

    public void SetIsInstalling(bool value) => IsInstalling = value;
    public void SetStatus(UiText value) => SetText(ref _installationStatus, value, nameof(InstallationStatus));
    public void SetCurrentName(UiText value) => SetText(ref _currentInstallationName, value, nameof(CurrentInstallationName));

    // Mirrors ReportViewModel.HasReport so the step view can offer "View report"
    // without this view model depending on the report step.
    public bool HasReport
    {
        get => _hasReport;
        private set => SetField(ref _hasReport, value);
    }

    public void SetHasReport(bool value) => HasReport = value;

    public void Reset()
    {
        if (IsInstalling)
        {
            return;
        }

        DisposeSession();
        InstallationItems.Clear();
        SetStatus(UiText.Key("Installation.Status.Ready"));
        SetCurrentName(UiText.Key("Installation.Current.Waiting"));
        RaiseSummary();
    }

    public async Task<bool> StartAsync(
        IReadOnlyList<InstallationSelection> selections,
        IReadOnlyDictionary<string, CatalogueApplication> catalogueById,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalling || selections.Count == 0 || _session.Catalogue is null)
        {
            return false;
        }

        Starting?.Invoke(this, EventArgs.Empty);

        DisposeSession();
        InstallationItems.Clear();

        var verifier = new SoftwareInventoryInstallationVerifier(
            _softwareInventory,
            _session.Catalogue.DetectionTargets,
            _softwareStateResolver);
        _installationOrchestrator = new InstallationOrchestrator(
            [new WinGetProvider()],
            verifier);
        _installationSession = _installationOrchestrator.CreateSession(selections);
        _installationSession.ProgressChanged += InstallationSession_OnProgressChanged;

        foreach (var item in _installationSession.Items)
        {
            var application = catalogueById[item.ApplicationId];
            var row = new InstallationRowViewModel(
                item.ApplicationId,
                application.Name,
                BuildInitials(application.Name));
            row.Apply(item);
            InstallationItems.Add(row);
        }

        IsInstalling = true;
        SetStatus(UiText.Key("Installation.Status.Installing"));
        SetCurrentName(UiText.Key("Installation.Current.Preparing"));
        RaiseSummary();

        try
        {
            var report = await _installationOrchestrator
                .RunAsync(_installationSession, cancellationToken)
                .ConfigureAwait(true);
            ApplyReport(report);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(UiText.Key("Installation.Status.Cancelled"));
            if (_installationSession is not null)
            {
                ApplyReport(_installationSession.CreateReport());
            }
            return false;
        }
        finally
        {
            IsInstalling = false;
            RaiseSummary();
        }
    }

    public void Cancel()
    {
        if (!IsInstalling)
        {
            return;
        }

        SetStatus(UiText.Key("Installation.Status.Cancelling"));
        _installationSession?.Cancel();
    }

    public async Task<bool> RetryAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        if (IsInstalling || _installationOrchestrator is null || _installationSession is null)
        {
            return false;
        }

        var row = InstallationItems.FirstOrDefault(item =>
            string.Equals(item.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        if (row is not { CanRetry: true })
        {
            return false;
        }

        IsInstalling = true;
        SetStatus(UiText.Key("Installation.Status.Retrying", row.Name));
        SetCurrentName(UiText.Raw(row.Name));

        try
        {
            var report = await _installationOrchestrator
                .RetryAsync(_installationSession, applicationId, cancellationToken)
                .ConfigureAwait(true);
            ApplyReport(report);
            return true;
        }
        finally
        {
            IsInstalling = false;
            RaiseSummary();
        }
    }

    public void RaiseSummary()
    {
        OnPropertyChanged(nameof(InstallationCompletedCount));
        OnPropertyChanged(nameof(InstallationFailedCount));
        OnPropertyChanged(nameof(InstallationRemainingCount));
        OnPropertyChanged(nameof(InstallationTotalCount));
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in InstallationItems)
        {
            row.RefreshText();
        }
    }

    public void Dispose()
    {
        if (IsInstalling)
        {
            _installationSession?.Cancel();
        }

        DisposeSession();
    }

    private void ApplyReport(InstallationReport report)
    {
        foreach (var snapshot in report.Items)
        {
            var row = InstallationItems.FirstOrDefault(item =>
                string.Equals(item.ApplicationId, snapshot.ApplicationId, StringComparison.OrdinalIgnoreCase));
            row?.Apply(snapshot);
        }

        _session.SetLastReport(report);
        Completed?.Invoke(this, report);

        SetStatus(UiText.Key(report.State == InstallationSessionState.Cancelled
            ? "Installation.Status.CancelledReview"
            : report.FailedCount > 0
                ? "Installation.Status.CompletedIssues"
                : "Installation.Status.CompletedSuccess"));
        SetCurrentName(UiText.Key("Installation.Current.Complete"));
        RaiseSummary();
    }

    private void InstallationSession_OnProgressChanged(object? sender, InstallationProgressEvent e)
    {
        _dispatcher.Post(() => ApplyProgress(e));
    }

    private void ApplyProgress(InstallationProgressEvent progress)
    {
        if (progress.Item is not null)
        {
            var row = InstallationItems.FirstOrDefault(item =>
                string.Equals(item.ApplicationId, progress.Item.ApplicationId, StringComparison.OrdinalIgnoreCase));
            row?.Apply(progress.Item);
            if (progress.Item.State == InstallationQueueItemState.Running)
            {
                SetCurrentName(UiText.Raw(row?.Name ?? progress.Item.ApplicationId));
            }
        }

        // Known session codes are translated; other progress messages come from the engine as-is.
        SetStatus(progress.Code switch
        {
            "session.started" => UiText.Key("Installation.Status.Started"),
            "session.completed" => UiText.Key("Installation.Status.QueueCompleted"),
            "session.cancelled" => UiText.Key("Installation.Status.QueueCancelled"),
            "session.cancellation-requested" => UiText.Key("Installation.Status.CancellationRequested"),
            _ => UiText.Raw(progress.Message)
        });
        RaiseSummary();
    }

    private void DisposeSession()
    {
        if (_installationSession is null)
        {
            return;
        }

        _installationSession.ProgressChanged -= InstallationSession_OnProgressChanged;
        _installationSession.Dispose();
        _installationSession = null;
        _installationOrchestrator = null;
    }

    private static string BuildInitials(string name)
    {
        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        return words.Length switch
        {
            0 => "•",
            1 => words[0][..1].ToUpperInvariant(),
            _ => string.Concat(words.Select(word => char.ToUpperInvariant(word[0])))
        };
    }
}
