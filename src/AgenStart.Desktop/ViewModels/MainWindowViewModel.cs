using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Application.Installation;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels.Steps;
using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.Inventory;
using AgenStart.Platform.Windows.SoftwareInventory;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Desktop.ViewModels;

public enum AppStep
{
    Overview,
    YourPc,
    UsageProfile,
    Recommendations,
    Review,
    Installation,
    Report,
    History,
    Settings
}

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISessionState _session;
    private bool _disposed;
    private AppStep _currentStep = AppStep.Overview;

    public MainWindowViewModel(
        IMachineInventoryProvider? machineInventory = null,
        IInstalledSoftwareInventoryProvider? softwareInventory = null,
        SoftwareStateResolver? softwareStateResolver = null,
        RecommendationEngine? recommendationEngine = null,
        SoftwareCatalogueLoader? catalogueLoader = null,
        ISessionState? session = null,
        IUiDispatcher? dispatcher = null)
    {
        var machine = machineInventory ?? new WindowsMachineInventoryProvider();
        var software = softwareInventory ?? new WindowsInstalledSoftwareInventoryProvider();
        var resolver = softwareStateResolver ?? new SoftwareStateResolver();
        var engine = recommendationEngine ?? new RecommendationEngine();
        var loader = catalogueLoader ?? new SoftwareCatalogueLoader();
        _session = session ?? new SessionState();
        var ui = dispatcher ?? AvaloniaUiDispatcher.Shared;

        Report = new ReportViewModel();
        Report.PropertyChanged += RelayChildPropertyChanged;
        Report.ReportItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ReportItems));

        Installation = new InstallationViewModel(_session, ui, software, resolver);
        Installation.PropertyChanged += RelayChildPropertyChanged;
        Installation.InstallationItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(InstallationItems));
        Installation.Starting += Installation_OnStarting;
        Installation.Completed += Installation_OnCompleted;

        Review = new ReviewViewModel();
        Review.PropertyChanged += RelayChildPropertyChanged;
        Review.ReviewItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ReviewItems));

        RecommendationsStep = new RecommendationsViewModel(software, resolver, engine, loader, _session);
        RecommendationsStep.PropertyChanged += RelayChildPropertyChanged;
        RecommendationsStep.Recommendations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Recommendations));
        RecommendationsStep.SelectionChanged += (_, _) => ResetReviewOnly();

        UsageProfile = new UsageProfileViewModel();
        UsageProfile.PropertyChanged += RelayChildPropertyChanged;

        Overview = new OverviewViewModel(machine, _session);
        Overview.PropertyChanged += RelayChildPropertyChanged;
    }

    public ReportViewModel Report { get; }
    public InstallationViewModel Installation { get; }
    public ReviewViewModel Review { get; }
    public RecommendationsViewModel RecommendationsStep { get; }
    public UsageProfileViewModel UsageProfile { get; }
    public OverviewViewModel Overview { get; }

    private void RelayChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null)
        {
            return;
        }

        OnPropertyChanged(e.PropertyName);

        if (e.PropertyName is nameof(OverviewViewModel.IsBusy)
            or nameof(RecommendationsViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(IsBusy));
        }

        if (e.PropertyName is nameof(InstallationViewModel.IsInstalling)
            or nameof(ReviewViewModel.HasReview)
            or nameof(ReviewViewModel.AgreementsAccepted)
            or nameof(ReviewViewModel.ReviewInstallCount))
        {
            OnPropertyChanged(nameof(CanStartInstallation));
        }

        if (e.PropertyName is nameof(InstallationViewModel.IsInstalling)
            or nameof(RecommendationsViewModel.HasRecommendations)
            or nameof(RecommendationsViewModel.SelectedCount))
        {
            OnPropertyChanged(nameof(CanReview));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RecommendationRowViewModel> Recommendations => RecommendationsStep.Recommendations;
    public ObservableCollection<ReviewRowViewModel> ReviewItems => Review.ReviewItems;
    public ObservableCollection<InstallationRowViewModel> InstallationItems => Installation.InstallationItems;
    public ObservableCollection<ReportRowViewModel> ReportItems => Report.ReportItems;

    public AppStep CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (SetField(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsOverview));
                OnPropertyChanged(nameof(IsYourPc));
                OnPropertyChanged(nameof(IsUsageProfile));
                OnPropertyChanged(nameof(IsRecommendations));
                OnPropertyChanged(nameof(IsReview));
                OnPropertyChanged(nameof(IsInstallationStep));
                OnPropertyChanged(nameof(IsReport));
                OnPropertyChanged(nameof(IsHistory));
                OnPropertyChanged(nameof(IsSettings));
            }
        }
    }

    public bool IsOverview => _currentStep == AppStep.Overview;
    public bool IsYourPc => _currentStep == AppStep.YourPc;
    public bool IsUsageProfile => _currentStep == AppStep.UsageProfile;
    public bool IsRecommendations => _currentStep == AppStep.Recommendations;
    public bool IsReview => _currentStep == AppStep.Review;
    public bool IsInstallationStep => _currentStep == AppStep.Installation;
    public bool IsReport => _currentStep == AppStep.Report;
    public bool IsHistory => _currentStep == AppStep.History;
    public bool IsSettings => _currentStep == AppStep.Settings;

    public void ShowStep(AppStep step) => CurrentStep = step;

    public bool IsBusy => Overview.IsBusy || RecommendationsStep.IsBusy;

    public bool HasSnapshot => Overview.HasSnapshot;
    public bool HasRecommendations => RecommendationsStep.HasRecommendations;
    public bool HasReview => Review.HasReview;
    public bool IsInstalling => Installation.IsInstalling;
    public bool HasReport => Report.HasReport;

    public bool AgreementsAccepted
    {
        get => Review.AgreementsAccepted;
        set => Review.AgreementsAccepted = value;
    }

    public UserProfile SelectedProfile => UsageProfile.SelectedProfile;
    public string SelectedProfileName => UsageProfile.SelectedProfileName;
    public string SelectedProfileDescription => UsageProfile.SelectedProfileDescription;

    public string RecommendationStatus
    {
        get => RecommendationsStep.RecommendationStatus;
        private set => RecommendationsStep.RecommendationStatus = value;
    }

    public string InstallationStatus => Installation.InstallationStatus;
    public string CurrentInstallationName => Installation.CurrentInstallationName;
    public string ReportStatus => Report.ReportStatus;
    public string ReportTitle => Report.ReportTitle;
    public string ReportCompletedAt => Report.ReportCompletedAt;

    public int RecommendationCount => RecommendationsStep.RecommendationCount;
    public int SelectedCount => RecommendationsStep.SelectedCount;
    public int AlreadyInstalledCount => RecommendationsStep.AlreadyInstalledCount;
    public int OptionalCount => RecommendationsStep.OptionalCount;
    public bool CanReview => HasRecommendations && SelectedCount > 0 && !IsInstalling;

    public int ReviewInstallCount => Review.ReviewInstallCount;
    public int ReviewAlreadyInstalledCount => Review.ReviewAlreadyInstalledCount;
    public int ReviewTotalCount => Review.ReviewTotalCount;
    public bool CanStartInstallation => HasReview && ReviewInstallCount > 0 && AgreementsAccepted && !IsInstalling;

    public int InstallationCompletedCount => Installation.InstallationCompletedCount;
    public int InstallationFailedCount => Installation.InstallationFailedCount;
    public int InstallationRemainingCount => Installation.InstallationRemainingCount;
    public int InstallationTotalCount => Installation.InstallationTotalCount;

    public int ReportProcessedCount => Report.ReportProcessedCount;
    public int ReportInstalledCount => Report.ReportInstalledCount;
    public int ReportAlreadyInstalledCount => Report.ReportAlreadyInstalledCount;
    public int ReportFailedCount => Report.ReportFailedCount;
    public bool ReportRequiresReboot => Report.ReportRequiresReboot;

    public string StatusTitle => Overview.StatusTitle;
    public string StatusMessage => Overview.StatusMessage;
    public string OperatingSystem => Overview.OperatingSystem;
    public string Cpu => Overview.Cpu;
    public string Memory => Overview.Memory;
    public string Gpu => Overview.Gpu;
    public string SystemDrive => Overview.SystemDrive;
    public string FreeSpace => Overview.FreeSpace;
    public string Architecture => Overview.Architecture;
    public string WinGet => Overview.WinGet;
    public string WinGetVersion => Overview.WinGetVersion;
    public string AnalysisDiagnostic => Overview.AnalysisDiagnostic;

    public void SelectProfile(UserProfile profile)
    {
        if (IsInstalling || !UsageProfile.TrySelect(profile))
        {
            return;
        }

        ClearRecommendations();
        RecommendationStatus = "Profile changed. Build recommendations when you're ready.";
    }

    public Task AnalyzeAsync(CancellationToken cancellationToken = default) =>
        Overview.AnalyzeAsync(cancellationToken);

    public Task BuildRecommendationsAsync(CancellationToken cancellationToken = default) =>
        RecommendationsStep.BuildAsync(SelectedProfile, SelectedProfileName, cancellationToken);

    public void SelectEssentialsOnly() => RecommendationsStep.SelectEssentialsOnly();

    public bool PrepareReview()
    {
        if (!CanReview || _session.Catalogue is null)
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

        Review.SetHasReview(ReviewItems.Any(row => row.WillInstall));
        AgreementsAccepted = false;
        Review.RaiseSummary();
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
        if (!CanStartInstallation || _session.Catalogue is null)
        {
            return false;
        }

        var selectedRows = Recommendations.Where(row => row.IsSelected).ToArray();
        var catalogueById = _session.Catalogue.Applications.ToDictionary(
            application => application.Id,
            StringComparer.OrdinalIgnoreCase);
        var selections = new List<InstallationSelection>(selectedRows.Length);

        foreach (var row in selectedRows)
        {
            if (!catalogueById.TryGetValue(row.ApplicationId, out var application) || application.WindowsPackage is null)
            {
                Installation.SetStatus($"{row.Name} has no trusted Windows package mapping and cannot be installed.");
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

        return await Installation.StartAsync(selections, catalogueById, cancellationToken).ConfigureAwait(true);
    }

    public void CancelInstallation() => Installation.Cancel();

    public Task<bool> RetryInstallationAsync(string applicationId, CancellationToken cancellationToken = default) =>
        Installation.RetryAsync(applicationId, cancellationToken);

    private void Installation_OnStarting(object? sender, EventArgs e) => Report.Reset();

    private void Installation_OnCompleted(object? sender, InstallationReport report)
    {
        if (_session.Catalogue is null)
        {
            return;
        }

        Report.Apply(
            report,
            _session.Catalogue,
            Recommendations.Where(row => row.Disposition == RecommendationDisposition.AlreadyInstalled));
    }

    private void ClearRecommendations()
    {
        RecommendationsStep.ClearRows();
        _session.SetCatalogue(null);
        ResetInstallationFlow();
        RecommendationsStep.RaiseSummary();
        OnPropertyChanged(nameof(CanReview));
    }

    private void ResetReviewOnly()
    {
        if (IsInstalling)
        {
            return;
        }

        Review.Reset();
        Review.RaiseSummary();
    }

    private void ResetInstallationFlow()
    {
        if (IsInstalling)
        {
            return;
        }

        ResetReviewOnly();
        Installation.Reset();
        Report.Reset();
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
        Installation.Dispose();
    }
}
