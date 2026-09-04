using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using AgenStart.Application.Installation;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.Inventory;
using AgenStart.Platform.Windows.SoftwareInventory;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMachineInventoryProvider _machineInventory;
    private readonly IInstalledSoftwareInventoryProvider _softwareInventory;
    private readonly SoftwareStateResolver _softwareStateResolver;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly SoftwareCatalogueLoader _catalogueLoader;

    private MachineSnapshot? _machineSnapshot;
    private SoftwareCatalogue? _catalogue;
    private InstallationOrchestrator? _installationOrchestrator;
    private InstallationSession? _installationSession;
    private InstallationReport? _installationReport;
    private bool _disposed;

    private bool _isBusy;
    private bool _hasSnapshot;
    private bool _hasRecommendations;
    private bool _hasReview;
    private bool _isInstalling;
    private bool _hasReport;
    private bool _agreementsAccepted;
    private UserProfile _selectedProfile = UserProfile.Development;

    private string _recommendationStatus = "Choose a usage profile to build recommendations.";
    private string _installationStatus = "Ready to install the approved setup.";
    private string _currentInstallationName = "Waiting to start";
    private string _reportStatus = "No installation report yet.";
    private string _reportCompletedAt = "—";
    private string _statusTitle = "Ready for analysis";
    private string _statusMessage = "AgenStart can inspect the essential capabilities of this PC locally.";
    private string _operatingSystem = "Not analysed";
    private string _cpu = "Not analysed";
    private string _memory = "Not analysed";
    private string _gpu = "Not analysed";
    private string _systemDrive = "Not analysed";
    private string _freeSpace = "Not analysed";
    private string _architecture = "Not analysed";
    private string _winGet = "Not analysed";
    private string _winGetVersion = "Not analysed";
    private string _analysisDiagnostic = "No personal files or account information are inspected.";

    public MainWindowViewModel(
        IMachineInventoryProvider? machineInventory = null,
        IInstalledSoftwareInventoryProvider? softwareInventory = null,
        SoftwareStateResolver? softwareStateResolver = null,
        RecommendationEngine? recommendationEngine = null,
        SoftwareCatalogueLoader? catalogueLoader = null)
    {
        _machineInventory = machineInventory ?? new WindowsMachineInventoryProvider();
        _softwareInventory = softwareInventory ?? new WindowsInstalledSoftwareInventoryProvider();
        _softwareStateResolver = softwareStateResolver ?? new SoftwareStateResolver();
        _recommendationEngine = recommendationEngine ?? new RecommendationEngine();
        _catalogueLoader = catalogueLoader ?? new SoftwareCatalogueLoader();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RecommendationRowViewModel> Recommendations { get; } = [];
    public ObservableCollection<ReviewRowViewModel> ReviewItems { get; } = [];
    public ObservableCollection<InstallationRowViewModel> InstallationItems { get; } = [];
    public ObservableCollection<ReportRowViewModel> ReportItems { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool HasSnapshot
    {
        get => _hasSnapshot;
        private set => SetField(ref _hasSnapshot, value);
    }

    public bool HasRecommendations
    {
        get => _hasRecommendations;
        private set => SetField(ref _hasRecommendations, value);
    }

    public bool HasReview
    {
        get => _hasReview;
        private set => SetField(ref _hasReview, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetField(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(CanStartInstallation));
            }
        }
    }

    public bool HasReport
    {
        get => _hasReport;
        private set => SetField(ref _hasReport, value);
    }

    public bool AgreementsAccepted
    {
        get => _agreementsAccepted;
        set
        {
            if (SetField(ref _agreementsAccepted, value))
            {
                OnPropertyChanged(nameof(CanStartInstallation));
            }
        }
    }

    public UserProfile SelectedProfile
    {
        get => _selectedProfile;
        private set
        {
            if (!SetField(ref _selectedProfile, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(SelectedProfileDescription));
        }
    }

    public string SelectedProfileName => SelectedProfile.ToString();

    public string SelectedProfileDescription => SelectedProfile switch
    {
        UserProfile.Personal => "Everyday apps, browsing, communication and media.",
        UserProfile.Development => "Code, databases, terminals and developer tools.",
        UserProfile.Business => "Office, communication, productivity and collaboration.",
        UserProfile.Creation => "Design, media, content and creative workflows.",
        UserProfile.Training => "Learning, course tools and guided study setups.",
        _ => string.Empty
    };

    public string RecommendationStatus
    {
        get => _recommendationStatus;
        private set => SetField(ref _recommendationStatus, value);
    }

    public string InstallationStatus
    {
        get => _installationStatus;
        private set => SetField(ref _installationStatus, value);
    }

    public string CurrentInstallationName
    {
        get => _currentInstallationName;
        private set => SetField(ref _currentInstallationName, value);
    }

    public string ReportStatus
    {
        get => _reportStatus;
        private set => SetField(ref _reportStatus, value);
    }

    public string ReportCompletedAt
    {
        get => _reportCompletedAt;
        private set => SetField(ref _reportCompletedAt, value);
    }

    public int RecommendationCount => Recommendations.Count;
    public int SelectedCount => Recommendations.Count(row => row.IsSelected);
    public int AlreadyInstalledCount => Recommendations.Count(row => row.Disposition == RecommendationDisposition.AlreadyInstalled);
    public int OptionalCount => Recommendations.Count(row => row.Level == RecommendationLevel.Optional && row.Disposition == RecommendationDisposition.Recommended);
    public bool CanReview => HasRecommendations && SelectedCount > 0 && !IsInstalling;

    public int ReviewInstallCount => ReviewItems.Count(row => row.WillInstall);
    public int ReviewAlreadyInstalledCount => ReviewItems.Count(row => row.AlreadyInstalled);
    public int ReviewTotalCount => ReviewItems.Count;
    public bool CanStartInstallation => HasReview && ReviewInstallCount > 0 && AgreementsAccepted && !IsInstalling;

    public int InstallationCompletedCount => InstallationItems.Count(row => row.State == InstallationQueueItemState.Succeeded);
    public int InstallationFailedCount => InstallationItems.Count(row => row.State == InstallationQueueItemState.Failed);
    public int InstallationRemainingCount => InstallationItems.Count(row => row.State is InstallationQueueItemState.Queued or InstallationQueueItemState.Running);
    public int InstallationTotalCount => InstallationItems.Count;

    public int ReportProcessedCount => ReportItems.Count;
    public int ReportInstalledCount => _installationReport?.Items.Count(item =>
        item.State == InstallationQueueItemState.Succeeded &&
        item.LastOperationStatus != PackageOperationStatus.AlreadyInstalled) ?? 0;
    public int ReportAlreadyInstalledCount => ReportItems.Count(row => row.Result == "Already installed");
    public int ReportFailedCount => _installationReport?.FailedCount ?? 0;
    public bool ReportRequiresReboot => _installationReport?.Items.Any(item => item.RequiresReboot) == true;

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetField(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string OperatingSystem
    {
        get => _operatingSystem;
        private set => SetField(ref _operatingSystem, value);
    }

    public string Cpu
    {
        get => _cpu;
        private set => SetField(ref _cpu, value);
    }

    public string Memory
    {
        get => _memory;
        private set => SetField(ref _memory, value);
    }

    public string Gpu
    {
        get => _gpu;
        private set => SetField(ref _gpu, value);
    }

    public string SystemDrive
    {
        get => _systemDrive;
        private set => SetField(ref _systemDrive, value);
    }

    public string FreeSpace
    {
        get => _freeSpace;
        private set => SetField(ref _freeSpace, value);
    }

    public string Architecture
    {
        get => _architecture;
        private set => SetField(ref _architecture, value);
    }

    public string WinGet
    {
        get => _winGet;
        private set => SetField(ref _winGet, value);
    }

    public string WinGetVersion
    {
        get => _winGetVersion;
        private set => SetField(ref _winGetVersion, value);
    }

    public string AnalysisDiagnostic
    {
        get => _analysisDiagnostic;
        private set => SetField(ref _analysisDiagnostic, value);
    }

    public void SelectProfile(UserProfile profile)
    {
        if (SelectedProfile == profile || IsInstalling)
        {
            return;
        }

        SelectedProfile = profile;
        ClearRecommendations();
        RecommendationStatus = "Profile changed. Build recommendations when you're ready.";
    }

    public async Task AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsInstalling)
        {
            return;
        }

        IsBusy = true;
        StatusTitle = "Analysing this PC";
        StatusMessage = "Reading essential system capabilities locally…";

        try
        {
            var snapshot = await _machineInventory.CaptureAsync(cancellationToken).ConfigureAwait(true);
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusTitle = "Analysis cancelled";
            StatusMessage = "No changes were made to this PC.";
        }
        catch (Exception)
        {
            StatusTitle = "Analysis unavailable";
            StatusMessage = "AgenStart could not complete the local machine analysis.";
            AnalysisDiagnostic = "Try the analysis again. No machine changes were made.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BuildRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsInstalling || _machineSnapshot is null)
        {
            return;
        }

        IsBusy = true;
        RecommendationStatus = "Checking installed software and building recommendations…";

        try
        {
            var cataloguePath = Path.Combine(AppContext.BaseDirectory, "Data", "catalogue.json");
            using var catalogueStream = File.OpenRead(cataloguePath);
            var catalogue = _catalogueLoader.Load(catalogueStream);

            var installedSoftware = await _softwareInventory
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(true);
            var softwareState = _softwareStateResolver.Resolve(catalogue.DetectionTargets, installedSoftware);
            var plan = _recommendationEngine.Build(new RecommendationRequest(
                SelectedProfile,
                _machineSnapshot,
                softwareState,
                catalogue.Definitions));

            ClearRecommendations();
            _catalogue = catalogue;
            var metadata = catalogue.Applications.ToDictionary(
                application => application.Id,
                StringComparer.OrdinalIgnoreCase);

            foreach (var decision in plan.Decisions)
            {
                var description = metadata.TryGetValue(decision.ApplicationId, out var application)
                    ? application.Description
                    : string.Empty;
                var row = new RecommendationRowViewModel(decision, description);
                row.PropertyChanged += RecommendationRow_OnPropertyChanged;
                Recommendations.Add(row);
            }

            HasRecommendations = true;
            RecommendationStatus = $"Based on your {SelectedProfileName} profile, machine capabilities and software already installed.";
            RaiseRecommendationSummary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RecommendationStatus = "Recommendation analysis cancelled.";
        }
        catch (Exception)
        {
            ClearRecommendations();
            RecommendationStatus = "AgenStart could not build recommendations from the local catalogue and inventory.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectEssentialsOnly()
    {
        if (IsInstalling)
        {
            return;
        }

        foreach (var row in Recommendations.Where(row => row.CanSelect))
        {
            row.IsSelected = row.Level == RecommendationLevel.Essential;
        }

        RaiseRecommendationSummary();
    }

    public bool PrepareReview()
    {
        if (!CanReview || _catalogue is null)
        {
            return false;
        }

        ResetInstallationFlow();
        ReviewItems.Clear();

        foreach (var row in Recommendations.Where(row =>
                     row.IsSelected || row.Disposition == RecommendationDisposition.AlreadyInstalled))
        {
            ReviewItems.Add(new ReviewRowViewModel(
                row.ApplicationId,
                row.Name,
                row.Initials,
                row.Reason,
                row.IsSelected,
                row.Disposition == RecommendationDisposition.AlreadyInstalled));
        }

        HasReview = ReviewItems.Any(row => row.WillInstall);
        AgreementsAccepted = false;
        RaiseReviewSummary();
        return HasReview;
    }

    public void RemoveFromReview(string applicationId)
    {
        if (IsInstalling || string.IsNullOrWhiteSpace(applicationId))
        {
            return;
        }

        var recommendation = Recommendations.FirstOrDefault(row =>
            string.Equals(row.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        if (recommendation is { CanSelect: true })
        {
            recommendation.IsSelected = false;
        }

        PrepareReview();
    }

    public async Task<bool> StartInstallationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStartInstallation || _catalogue is null)
        {
            return false;
        }

        var selectedRows = Recommendations.Where(row => row.IsSelected).ToArray();
        var catalogueById = _catalogue.Applications.ToDictionary(
            application => application.Id,
            StringComparer.OrdinalIgnoreCase);
        var selections = new List<InstallationSelection>(selectedRows.Length);

        foreach (var row in selectedRows)
        {
            if (!catalogueById.TryGetValue(row.ApplicationId, out var application) || application.WindowsPackage is null)
            {
                InstallationStatus = $"{row.Name} has no trusted Windows package mapping and cannot be installed.";
                return false;
            }

            selections.Add(new InstallationSelection(
                row.ApplicationId,
                application.WindowsPackage,
                Approved: true,
                Silent: true,
                AcceptPackageAgreements: AgreementsAccepted,
                AcceptSourceAgreements: AgreementsAccepted));
        }

        if (selections.Count == 0)
        {
            return false;
        }

        DisposeInstallationSession();
        InstallationItems.Clear();
        ReportItems.Clear();
        _installationReport = null;
        HasReport = false;

        var verifier = new SoftwareInventoryInstallationVerifier(
            _softwareInventory,
            _catalogue.DetectionTargets,
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
        InstallationStatus = "AgenStart is installing only the applications you approved.";
        CurrentInstallationName = "Preparing installation queue";
        RaiseInstallationSummary();

        try
        {
            var report = await _installationOrchestrator
                .RunAsync(_installationSession, cancellationToken)
                .ConfigureAwait(true);
            ApplyInstallationReport(report);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            InstallationStatus = "Installation cancelled.";
            if (_installationSession is not null)
            {
                ApplyInstallationReport(_installationSession.CreateReport());
            }
            return false;
        }
        finally
        {
            IsInstalling = false;
            RaiseInstallationSummary();
        }
    }

    public void CancelInstallation()
    {
        if (!IsInstalling)
        {
            return;
        }

        InstallationStatus = "Cancelling safely after the current provider operation…";
        _installationSession?.Cancel();
    }

    public async Task<bool> RetryInstallationAsync(
        string applicationId,
        CancellationToken cancellationToken = default)
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
        InstallationStatus = $"Retrying {row.Name}.";
        CurrentInstallationName = row.Name;

        try
        {
            var report = await _installationOrchestrator
                .RetryAsync(_installationSession, applicationId, cancellationToken)
                .ConfigureAwait(true);
            ApplyInstallationReport(report);
            return true;
        }
        finally
        {
            IsInstalling = false;
            RaiseInstallationSummary();
        }
    }

    private void RecommendationRow_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecommendationRowViewModel.IsSelected))
        {
            RaiseRecommendationSummary();
            ResetReviewOnly();
        }
    }

    private void InstallationSession_OnProgressChanged(object? sender, InstallationProgressEvent e)
    {
        Dispatcher.UIThread.Post(() => ApplyInstallationProgress(e));
    }

    private void ApplyInstallationProgress(InstallationProgressEvent progress)
    {
        if (progress.Item is not null)
        {
            var row = InstallationItems.FirstOrDefault(item =>
                string.Equals(item.ApplicationId, progress.Item.ApplicationId, StringComparison.OrdinalIgnoreCase));
            row?.Apply(progress.Item);
            if (progress.Item.State == InstallationQueueItemState.Running)
            {
                CurrentInstallationName = row?.Name ?? progress.Item.ApplicationId;
            }
        }

        InstallationStatus = progress.Code switch
        {
            "session.started" => "Installation started. Packages are resolved through trusted provider identities.",
            "session.completed" => "Installation queue completed.",
            "session.cancelled" => "Installation queue cancelled.",
            "session.cancellation-requested" => "Cancellation requested. Waiting for the provider operation to stop safely.",
            _ => progress.Message
        };
        RaiseInstallationSummary();
    }

    private void ApplyInstallationReport(InstallationReport report)
    {
        _installationReport = report;

        foreach (var snapshot in report.Items)
        {
            var row = InstallationItems.FirstOrDefault(item =>
                string.Equals(item.ApplicationId, snapshot.ApplicationId, StringComparison.OrdinalIgnoreCase));
            row?.Apply(snapshot);
        }

        BuildReportRows(report);
        HasReport = true;
        InstallationStatus = report.State == InstallationSessionState.Cancelled
            ? "Installation cancelled. Review the final state below."
            : report.FailedCount > 0
                ? "Installation completed with issues. Failed items can be retried when available."
                : "Installation completed successfully.";
        CurrentInstallationName = "Queue complete";
        ReportStatus = report.State == InstallationSessionState.Cancelled
            ? "The approved setup was cancelled before every item completed."
            : report.FailedCount > 0
                ? "AgenStart finished the approved setup with issues."
                : "AgenStart finished the approved setup.";
        ReportCompletedAt = report.CompletedAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
        RaiseInstallationSummary();
        RaiseReportSummary();
    }

    private void BuildReportRows(InstallationReport report)
    {
        ReportItems.Clear();
        if (_catalogue is null)
        {
            return;
        }

        var catalogueById = _catalogue.Applications.ToDictionary(
            application => application.Id,
            StringComparer.OrdinalIgnoreCase);
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in report.Items.OrderBy(item => item.Sequence))
        {
            if (!catalogueById.TryGetValue(item.ApplicationId, out var application))
            {
                continue;
            }

            var result = item.State switch
            {
                InstallationQueueItemState.Succeeded when item.LastOperationStatus == PackageOperationStatus.AlreadyInstalled => "Already installed",
                InstallationQueueItemState.Succeeded => "Installed",
                InstallationQueueItemState.Failed => "Failed",
                InstallationQueueItemState.Skipped => "Skipped",
                InstallationQueueItemState.Cancelled => "Cancelled",
                _ => item.State.ToString()
            };

            ReportItems.Add(new ReportRowViewModel(
                item.ApplicationId,
                application.Name,
                BuildInitials(application.Name),
                result,
                item.InstalledVersion,
                item.RequiresReboot));
            included.Add(item.ApplicationId);
        }

        foreach (var recommendation in Recommendations.Where(row =>
                     row.Disposition == RecommendationDisposition.AlreadyInstalled &&
                     !included.Contains(row.ApplicationId)))
        {
            ReportItems.Add(new ReportRowViewModel(
                recommendation.ApplicationId,
                recommendation.Name,
                recommendation.Initials,
                "Already installed",
                null,
                requiresReboot: false));
        }
    }

    private void ClearRecommendations()
    {
        foreach (var row in Recommendations)
        {
            row.PropertyChanged -= RecommendationRow_OnPropertyChanged;
        }

        Recommendations.Clear();
        _catalogue = null;
        HasRecommendations = false;
        ResetInstallationFlow();
        RaiseRecommendationSummary();
    }

    private void ResetReviewOnly()
    {
        if (IsInstalling)
        {
            return;
        }

        ReviewItems.Clear();
        HasReview = false;
        AgreementsAccepted = false;
        RaiseReviewSummary();
    }

    private void ResetInstallationFlow()
    {
        if (IsInstalling)
        {
            return;
        }

        ResetReviewOnly();
        DisposeInstallationSession();
        InstallationItems.Clear();
        ReportItems.Clear();
        _installationReport = null;
        HasReport = false;
        InstallationStatus = "Ready to install the approved setup.";
        CurrentInstallationName = "Waiting to start";
        ReportStatus = "No installation report yet.";
        ReportCompletedAt = "—";
        RaiseInstallationSummary();
        RaiseReportSummary();
    }

    private void DisposeInstallationSession()
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

    private void RaiseRecommendationSummary()
    {
        OnPropertyChanged(nameof(RecommendationCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(AlreadyInstalledCount));
        OnPropertyChanged(nameof(OptionalCount));
        OnPropertyChanged(nameof(CanReview));
    }

    private void RaiseReviewSummary()
    {
        OnPropertyChanged(nameof(ReviewInstallCount));
        OnPropertyChanged(nameof(ReviewAlreadyInstalledCount));
        OnPropertyChanged(nameof(ReviewTotalCount));
        OnPropertyChanged(nameof(CanStartInstallation));
    }

    private void RaiseInstallationSummary()
    {
        OnPropertyChanged(nameof(InstallationCompletedCount));
        OnPropertyChanged(nameof(InstallationFailedCount));
        OnPropertyChanged(nameof(InstallationRemainingCount));
        OnPropertyChanged(nameof(InstallationTotalCount));
    }

    private void RaiseReportSummary()
    {
        OnPropertyChanged(nameof(ReportProcessedCount));
        OnPropertyChanged(nameof(ReportInstalledCount));
        OnPropertyChanged(nameof(ReportAlreadyInstalledCount));
        OnPropertyChanged(nameof(ReportFailedCount));
        OnPropertyChanged(nameof(ReportRequiresReboot));
    }

    private void ApplySnapshot(MachineSnapshot snapshot)
    {
        _machineSnapshot = snapshot;
        HasSnapshot = snapshot.Platform.Kind == PlatformKind.Windows;
        ClearRecommendations();

        OperatingSystem = snapshot.Platform.Edition
            ?? (snapshot.Platform.Kind == PlatformKind.Windows ? "Windows" : "Unsupported platform");
        Cpu = snapshot.Cpu.Model ?? "CPU detected";
        Memory = FormatBytes(snapshot.Memory.TotalPhysicalBytes);
        Gpu = snapshot.Gpus.FirstOrDefault()?.Name ?? "Unknown";

        var systemDrive = snapshot.SystemDrive;
        SystemDrive = systemDrive is null
            ? "Unknown"
            : $"{FormatBytes(systemDrive.TotalBytes)} system drive";
        FreeSpace = systemDrive is null
            ? "Unknown"
            : $"{FormatBytes(systemDrive.AvailableBytes)} free";

        Architecture = FormatArchitecture(snapshot.Platform.Architecture);
        WinGet = snapshot.PackageManager.State == CapabilityState.Available
            ? "Available"
            : snapshot.PackageManager.State.ToString();
        WinGetVersion = snapshot.PackageManager.Version?.ToString() ?? "Unknown";

        if (snapshot.Platform.Kind == PlatformKind.Windows)
        {
            StatusTitle = "PC ready";
            StatusMessage = "Essential capabilities detected locally.";
            RecommendationStatus = "Choose a usage profile to build recommendations.";
        }
        else
        {
            StatusTitle = "Unsupported platform";
            StatusMessage = "The current AgenStart MVP supports Windows 10 and 11.";
        }

        AnalysisDiagnostic = snapshot.Diagnostics.Count == 0
            ? "No personal files or account information are inspected."
            : $"Analysis completed with {snapshot.Diagnostics.Count.ToString(CultureInfo.InvariantCulture)} diagnostic note(s). No personal files were inspected.";
    }

    private static string FormatArchitecture(MachineArchitecture architecture) => architecture switch
    {
        MachineArchitecture.X64 => "x64 architecture",
        MachineArchitecture.X86 => "x86 architecture",
        MachineArchitecture.Arm64 => "ARM64 architecture",
        _ => "Unknown architecture"
    };

    private static string FormatBytes(ulong? bytes)
    {
        if (bytes is null)
        {
            return "Unknown";
        }

        return FormatBytes((double)bytes.Value);
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null || bytes < 0)
        {
            return "Unknown";
        }

        return FormatBytes((double)bytes.Value);
    }

    private static string FormatBytes(double bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        return $"{Math.Round(bytes / gib):0} GB";
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsInstalling)
        {
            _installationSession?.Cancel();
        }

        DisposeInstallationSession();
    }
}
