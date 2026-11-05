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
    private readonly ILocalizer _text;
    private bool _disposed;
    private AppStep _currentStep = AppStep.Overview;

    public MainWindowViewModel(
        IMachineInventoryProvider? machineInventory = null,
        IInstalledSoftwareInventoryProvider? softwareInventory = null,
        SoftwareStateResolver? softwareStateResolver = null,
        RecommendationEngine? recommendationEngine = null,
        SoftwareCatalogueLoader? catalogueLoader = null,
        ISessionState? session = null,
        IUiDispatcher? dispatcher = null,
        ILocalizer? text = null)
    {
        var machine = machineInventory ?? new WindowsMachineInventoryProvider();
        var software = softwareInventory ?? new WindowsInstalledSoftwareInventoryProvider();
        var resolver = softwareStateResolver ?? new SoftwareStateResolver();
        var engine = recommendationEngine ?? new RecommendationEngine();
        var loader = catalogueLoader ?? new SoftwareCatalogueLoader();
        _session = session ?? new SessionState();
        _text = text ?? AvaloniaLocalizationService.Shared;
        var ui = dispatcher ?? AvaloniaUiDispatcher.Shared;

        Report = new ReportViewModel();

        Installation = new InstallationViewModel(_session, ui, software, resolver);
        Installation.Starting += Installation_OnStarting;
        Installation.Completed += Installation_OnCompleted;
        Report.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReportViewModel.HasReport))
            {
                Installation.SetHasReport(Report.HasReport);
            }
        };

        Review = new ReviewViewModel(_session);

        RecommendationsStep = new RecommendationsViewModel(software, resolver, engine, loader, _session);
        RecommendationsStep.SelectionChanged += (_, _) => ResetReviewOnly();

        UsageProfile = new UsageProfileViewModel();

        Overview = new OverviewViewModel(machine, _session);

        FlowTabs =
        [
            Tab(AppStep.Overview, "Shell.Overview", "\uE80F"),
            Tab(AppStep.YourPc, "Shell.YourPC", "\uE977"),
            Tab(AppStep.UsageProfile, "Shell.UsageProfile", "\uE77B"),
            Tab(AppStep.Recommendations, "Shell.Recommendations", "\uE734"),
            Tab(AppStep.Review, "Shell.Confirm", "\uE930"),
            Tab(AppStep.Installation, "Shell.Installation", "\uE896"),
            Tab(AppStep.Report, "Shell.Report", "\uE9F9")
        ];
        UtilityTabs =
        [
            Tab(AppStep.History, "Shell.History", "\uE81C"),
            Tab(AppStep.Settings, "Shell.Settings", "\uE713")
        ];

        // Any change in a step may change what can be opened; refreshing is a no-op when nothing moved.
        Overview.PropertyChanged += Step_OnPropertyChanged;
        RecommendationsStep.PropertyChanged += Step_OnPropertyChanged;
        Review.PropertyChanged += Step_OnPropertyChanged;
        Installation.PropertyChanged += Step_OnPropertyChanged;
        Report.PropertyChanged += Step_OnPropertyChanged;
        _text.LanguageChanged += Text_OnLanguageChanged;
        RefreshTabs();
    }

    public ReportViewModel Report { get; }
    public InstallationViewModel Installation { get; }
    public ReviewViewModel Review { get; }
    public RecommendationsViewModel RecommendationsStep { get; }
    public UsageProfileViewModel UsageProfile { get; }
    public OverviewViewModel Overview { get; }

    /// <summary>Setup flow tabs, left to right.</summary>
    public IReadOnlyList<NavigationTabViewModel> FlowTabs { get; }

    /// <summary>History and Settings, shown on the right of the tab bar.</summary>
    public IReadOnlyList<NavigationTabViewModel> UtilityTabs { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

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
                RefreshTabs();
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

    /// <summary>Flow navigation decided by the app (e.g. after a scan); not gated.</summary>
    public void ShowStep(AppStep step) => CurrentStep = step;

    public NavigationState Navigation => new(
        Overview.HasSnapshot,
        RecommendationsStep.HasRecommendations,
        Review.HasReview,
        Installation.InstallationTotalCount > 0 || Installation.IsInstalling,
        Report.HasReport);

    public bool CanOpen(AppStep step) => step == CurrentStep || NavigationGate.IsAvailable(step, Navigation);

    /// <summary>Navigation chosen by the user from a tab: a step that cannot be opened yet is a no-op.</summary>
    public bool SelectTab(AppStep step)
    {
        if (!CanOpen(step))
        {
            return false;
        }

        ShowStep(step);
        return true;
    }

    public void SelectProfile(UserProfile profile)
    {
        if (Installation.IsInstalling || !UsageProfile.TrySelect(profile))
        {
            return;
        }

        ClearRecommendations();
        RecommendationsStep.SetStatus(UiText.Key("Recommendations.Status.ProfileChanged"));
    }

    public Task BuildRecommendationsAsync(CancellationToken cancellationToken = default) =>
        RecommendationsStep.BuildAsync(UsageProfile.SelectedProfile, UsageProfile.SelectedProfileNameText, cancellationToken);

    public bool PrepareReview()
    {
        if (!RecommendationsStep.CanReview || _session.Catalogue is null)
        {
            return false;
        }

        ResetInstallationFlow();
        Review.ReviewItems.Clear();

        foreach (var row in RecommendationsStep.Recommendations.Where(row =>
                     row.IsSelected || row.Disposition == RecommendationDisposition.AlreadyInstalled))
        {
            Review.ReviewItems.Add(new ReviewRowViewModel(
                row.ApplicationId,
                row.Name,
                row.Initials,
                row.Reason,
                row.IsSelected,
                row.Disposition == RecommendationDisposition.AlreadyInstalled));
        }

        Review.SetHasReview(Review.ReviewItems.Any(row => row.WillInstall));
        Review.AgreementsAccepted = false;
        Review.RaiseSummary();
        return Review.HasReview;
    }

    public void RemoveFromReview(string applicationId)
    {
        if (Installation.IsInstalling || string.IsNullOrWhiteSpace(applicationId))
        {
            return;
        }

        var recommendation = RecommendationsStep.Recommendations.FirstOrDefault(row =>
            string.Equals(row.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        if (recommendation is { CanSelect: true })
        {
            recommendation.IsSelected = false;
        }

        PrepareReview();
    }

    public async Task<bool> StartInstallationAsync(CancellationToken cancellationToken = default)
    {
        if (!Review.CanStartInstallation || _session.Catalogue is null)
        {
            return false;
        }

        var selectedRows = RecommendationsStep.Recommendations.Where(row => row.IsSelected).ToArray();
        var catalogueById = _session.Catalogue.Applications.ToDictionary(
            application => application.Id,
            StringComparer.OrdinalIgnoreCase);
        var selections = new List<InstallationSelection>(selectedRows.Length);

        foreach (var row in selectedRows)
        {
            if (!catalogueById.TryGetValue(row.ApplicationId, out var application) || application.WindowsPackage is null)
            {
                Installation.SetStatus(UiText.Key("Installation.Status.NoMapping", row.Name));
                return false;
            }

            selections.Add(new InstallationSelection(
                row.ApplicationId,
                application.WindowsPackage,
                Approved: true,
                Silent: true,
                AcceptPackageAgreements: Review.AgreementsAccepted,
                AcceptSourceAgreements: Review.AgreementsAccepted));
        }

        if (selections.Count == 0)
        {
            return false;
        }

        return await Installation.StartAsync(selections, catalogueById, cancellationToken).ConfigureAwait(true);
    }

    private NavigationTabViewModel Tab(AppStep step, string labelKey, string icon) =>
        new(step, labelKey, icon, _text, selected => SelectTab(selected));

    private void RefreshTabs()
    {
        foreach (var tab in FlowTabs.Concat(UtilityTabs))
        {
            tab.Update(tab.Step == CurrentStep, CanOpen(tab.Step));
        }
    }

    private void Step_OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshTabs();

    private void Text_OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var tab in FlowTabs.Concat(UtilityTabs))
        {
            tab.RefreshText();
        }
    }

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
            RecommendationsStep.Recommendations.Where(row => row.Disposition == RecommendationDisposition.AlreadyInstalled));
    }

    private void ClearRecommendations()
    {
        RecommendationsStep.ClearRows();
        _session.SetCatalogue(null);
        ResetInstallationFlow();
        RecommendationsStep.RaiseSummary();
    }

    private void ResetReviewOnly()
    {
        if (Installation.IsInstalling)
        {
            return;
        }

        Review.Reset();
        Review.RaiseSummary();
    }

    private void ResetInstallationFlow()
    {
        if (Installation.IsInstalling)
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
        _text.LanguageChanged -= Text_OnLanguageChanged;
        Overview.PropertyChanged -= Step_OnPropertyChanged;
        RecommendationsStep.PropertyChanged -= Step_OnPropertyChanged;
        Review.PropertyChanged -= Step_OnPropertyChanged;
        Installation.PropertyChanged -= Step_OnPropertyChanged;
        Report.PropertyChanged -= Step_OnPropertyChanged;
        Installation.Dispose();
    }
}
