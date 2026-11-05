using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AgenStart.Application.Installation;
using AgenStart.Application.Profiles;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels;
using AgenStart.Desktop.ViewModels.Onboarding;
using AgenStart.Desktop.ViewModels.Steps;
using AgenStart.Recommendations;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private static readonly FilePickerFileType SetupProfileFileType = new("AgenStart setup")
    {
        Patterns = ["*.agenstart.json", "*.json"]
    };

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly MainWindowViewModel _viewModel;
    private readonly LocalExperienceViewModel _localExperience;
    private readonly OnboardingViewModel _onboarding;
    private bool _firstRun;
    private readonly SetupProfileSerializer _setupProfileSerializer = new();
    private readonly SoftwareCatalogueLoader _catalogueLoader = new();
    private Guid? _currentHistoryEntryId;
    private IDisposable? _multiProfileProgressSubscription;

    public MainWindow()
        : this(null)
    {
    }

    /// <param name="initialSettings">Settings App loaded at startup; null reads them from the local store.</param>
    public MainWindow(DesktopSettings? initialSettings)
    {
        _localExperience = new LocalExperienceViewModel(
            themes: AvaloniaThemeService.Shared,
            localization: AvaloniaLocalizationService.Shared,
            initialSettings: initialSettings);
        _viewModel = new MainWindowViewModel();
        _onboarding = new OnboardingViewModel(_localExperience, new ShellOnboardingActions(_viewModel))
        {
            Lifetime = _lifetimeCancellation.Token
        };

        InitializeComponent();
        FitToPrimaryScreen();
        DataContext = _viewModel;

        OnboardingControl.DataContext = _onboarding;
        _onboarding.Completed += Onboarding_OnCompleted;
        HistoryControl.DataContext = _localExperience;
        SettingsControl.DataContext = _localExperience;
        HistoryControl.ExportSetupRequested += HistoryView_OnExportSetupRequested;

        OverviewControl.SetImportSetupEnabled(StorageProvider.CanOpen);
        OverviewControl.AnalyzeRequested += OverviewView_OnAnalyzeRequested;
        OverviewControl.ImportSetupRequested += OverviewView_OnImportSetupRequested;

        ReportControl.FinishRequested += ReportView_OnFinishRequested;
        ReportControl.ExportSetupRequested += ReportView_OnExportSetupRequested;

        InstallationControl.CancelRequested += InstallationView_OnCancelRequested;
        InstallationControl.ViewReportRequested += InstallationView_OnViewReportRequested;
        InstallationControl.RetryRequested += InstallationView_OnRetryRequested;

        ReviewControl.StartInstallationRequested += ReviewView_OnStartInstallationRequested;
        ReviewControl.BackRequested += ReviewView_OnBackRequested;
        ReviewControl.RemoveRequested += ReviewView_OnRemoveRequested;

        YourPcControl.ContinueToProfileRequested += YourPcView_OnContinueToProfileRequested;
        YourPcControl.RefreshRequested += YourPcView_OnRefreshRequested;

        RecommendationsControl.ReviewRequested += RecommendationsView_OnReviewRequested;
        RecommendationsControl.SelectEssentialsRequested += RecommendationsView_OnSelectEssentialsRequested;

        MainScrollViewer.ScrollChanged += (_, _) => UpdateRecommendationStickyHeader();

        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        // Step state is observed on the step view models; the handled property names do not collide.
        _viewModel.Review.PropertyChanged += ViewModel_OnPropertyChanged;
        _viewModel.Installation.PropertyChanged += ViewModel_OnPropertyChanged;
        _viewModel.Report.PropertyChanged += ViewModel_OnPropertyChanged;
        _viewModel.RecommendationsStep.PropertyChanged += ViewModel_OnPropertyChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    // The design size (1600x1000) is larger than many laptop work areas; without a
    // clamp, CenterScreen still lets the window spill onto a neighbouring monitor.
    private void FitToPrimaryScreen()
    {
        if (Screens.Primary is not { } screen)
        {
            return;
        }

        const double margin = 0.92;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * margin));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * margin));
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        InstallMultiProfileExperience();

        try
        {
            await _localExperience.InitializeAsync(_lifetimeCancellation.Token);
            _firstRun = !_localExperience.OnboardingCompleted;
            await _onboarding.StartAsync(_firstRun);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private async void Onboarding_OnCompleted(object? sender, OnboardingOutcome outcome)
    {
        OnboardingControl.IsVisible = false;
        ShellRoot.IsVisible = true;
        UsageProfilesControl.SetSelection(_viewModel.UsageProfile.SelectedProfile);

        if (outcome == OnboardingOutcome.Recommendations)
        {
            _viewModel.ShowStep(AppStep.Recommendations);
            UpdateRecommendationStickyHeader();
            return;
        }

        _viewModel.ShowStep(AppStep.Overview);
        // A first run only ends on Home after a failed scan: do not scan again behind the user's back.
        if (_firstRun || !_localExperience.AnalyzeOnStartup || _viewModel.Overview.HasSnapshot)
        {
            return;
        }

        try
        {
            await _viewModel.Overview.AnalyzeAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private void InstallMultiProfileExperience()
    {
        UsageProfilesControl.SetSelection(_viewModel.UsageProfile.SelectedProfile);

        UsageProfilesControl.SelectionChanged += profiles =>
        {
            if (profiles == UserProfile.None)
            {
                return;
            }

            _viewModel.SelectProfile(profiles);
        };

        UsageProfilesControl.BuildRequested += async (_, _) =>
        {
            UsageProfilesControl.BeginRecommendationBuild();
            _viewModel.SelectProfile(UsageProfilesControl.SelectedProfiles);

            await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
            if (_viewModel.RecommendationsStep.HasRecommendations)
            {
                UsageProfilesControl.CompleteRecommendationBuild();
                _viewModel.ShowStep(AppStep.Recommendations);
                UpdateRecommendationStickyHeader();
            }
            else
            {
                UsageProfilesControl.FailRecommendationBuild(_viewModel.RecommendationsStep.RecommendationStatus);
            }
        };

        UsageProfilesControl.BackRequested += (_, _) => _viewModel.ShowStep(AppStep.YourPc);

        _multiProfileProgressSubscription = RecommendationPipelineDiagnostics.Subscribe(stage =>
            Dispatcher.UIThread.Post(() =>
            {
                _onboarding.UpdateBuildProgress(stage);
                if (_viewModel.RecommendationsStep.IsBusy && UsageProfilesControl.IsVisible)
                {
                    UsageProfilesControl.UpdateRecommendationProgress(stage);
                }
            }));
    }

    private async void OverviewView_OnAnalyzeRequested(object? sender, EventArgs e)
    {
        await _viewModel.Overview.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.Overview.HasSnapshot)
        {
            _viewModel.ShowStep(AppStep.YourPc);
        }
    }

    private async void YourPcView_OnRefreshRequested(object? sender, EventArgs e) =>
        await _viewModel.Overview.AnalyzeAsync(_lifetimeCancellation.Token);

    private void YourPcView_OnContinueToProfileRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.UsageProfile);

    private void RecommendationsView_OnSelectEssentialsRequested(object? sender, EventArgs e) =>
        _viewModel.RecommendationsStep.SelectEssentialsOnly();

    private void RecommendationsView_OnReviewRequested(object? sender, EventArgs e)
    {
        if (_viewModel.PrepareReview())
        {
            _viewModel.ShowStep(AppStep.Review);
        }
    }

    internal void UpdateRecommendationStickyHeader()
    {
        RecommendationStickyHeader.IsVisible =
            RecommendationsControl.IsVisible &&
            _viewModel.RecommendationsStep.HasRecommendations &&
            MainScrollViewer.Offset.Y >= 118;
    }

    private void ReviewView_OnBackRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.Recommendations);

    private void ReviewView_OnRemoveRequested(object? sender, ReviewRowViewModel row)
    {
        _viewModel.RemoveFromReview(row.ApplicationId);
        if (!_viewModel.Review.HasReview)
        {
            _viewModel.ShowStep(AppStep.Recommendations);
        }
    }

    private async void ReviewView_OnStartInstallationRequested(object? sender, EventArgs e)
    {
        if (!_viewModel.Review.CanStartInstallation)
        {
            return;
        }

        _currentHistoryEntryId = Guid.NewGuid();
        _viewModel.ShowStep(AppStep.Installation);
        await _viewModel.StartInstallationAsync(_lifetimeCancellation.Token);

        if (_viewModel.Report.HasReport)
        {
            await PersistCurrentReportAsync();
            if (_viewModel.Installation.InstallationFailedCount == 0)
            {
                _viewModel.ShowStep(AppStep.Report);
            }
        }
    }

    private void InstallationView_OnCancelRequested(object? sender, EventArgs e) =>
        _viewModel.Installation.Cancel();

    private void InstallationView_OnViewReportRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.Report);

    private async void InstallationView_OnRetryRequested(object? sender, InstallationRowViewModel row)
    {
        await _viewModel.Installation.RetryAsync(row.ApplicationId, _lifetimeCancellation.Token);
        if (_viewModel.Report.HasReport)
        {
            _currentHistoryEntryId ??= Guid.NewGuid();
            await PersistCurrentReportAsync();
            if (_viewModel.Installation.InstallationFailedCount == 0)
            {
                _viewModel.ShowStep(AppStep.Report);
            }
        }
    }

    private async Task PersistCurrentReportAsync()
    {
        if (!_viewModel.Report.HasReport || _currentHistoryEntryId is null)
        {
            return;
        }

        var applications = _viewModel.Report.ReportItems.Select(row => new SetupHistoryApplication(
            row.ApplicationId,
            row.Name,
            row.Result,
            row.InstalledVersion));
        var skippedCount = _viewModel.Installation.InstallationItems.Count(row => row.State == InstallationQueueItemState.Skipped);
        var cancelledCount = _viewModel.Installation.InstallationItems.Count(row => row.State == InstallationQueueItemState.Cancelled);

        try
        {
            await _localExperience.RecordSessionAsync(
                _currentHistoryEntryId.Value,
                _viewModel.UsageProfile.SelectedProfileName,
                _viewModel.Report.ReportProcessedCount,
                _viewModel.Report.ReportInstalledCount,
                _viewModel.Report.ReportAlreadyInstalledCount,
                _viewModel.Report.ReportFailedCount,
                skippedCount,
                cancelledCount,
                _viewModel.Report.ReportRequiresReboot,
                applications,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ReportView_OnFinishRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.Overview);

    private async void ReportView_OnExportSetupRequested(object? sender, EventArgs e)
    {
        if (!_viewModel.Report.HasReport || _viewModel.Review.ReviewItems.Count == 0)
        {
            return;
        }

        var applications = _viewModel.Review.ReviewItems.Select(row =>
            new SetupProfileApplication(row.ApplicationId, row.Reason));
        await SaveSetupProfileAsync(
            ToProfileId(_viewModel.UsageProfile.SelectedProfile),
            applications,
            Ui.Format("Setup.DisplayName", _viewModel.UsageProfile.SelectedProfileName));
    }

    private async void OverviewView_OnImportSetupRequested(object? sender, EventArgs e)
    {
        try
        {
            await ImportSetupAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            SetImportStatus(Ui["Import.ReadFailed"]);
        }
        catch (UnauthorizedAccessException)
        {
            SetImportStatus(Ui["Import.NoPermission"]);
        }
    }

    private async Task ImportSetupAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            SetImportStatus(Ui["Import.Unavailable"]);
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Ui["Import.DialogTitle"],
            AllowMultiple = false,
            FileTypeFilter = [SetupProfileFileType]
        });
        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenReadAsync();
        if (stream.CanSeek && stream.Length > SetupProfileSerializer.MaxDocumentBytes)
        {
            SetImportStatus(Ui["Import.TooLarge"]);
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var json = await reader.ReadToEndAsync(_lifetimeCancellation.Token);
        var readResult = _setupProfileSerializer.Deserialize(json);
        if (!readResult.IsValid || readResult.Profile is null)
        {
            // Validation messages come from the setup-profile reader and stay in English.
            SetImportStatus(readResult.Errors.FirstOrDefault()?.Message ?? Ui["Import.Invalid"]);
            return;
        }

        var profileDocument = readResult.Profile;
        if (!TryParseProfileId(profileDocument.ProfileId, out var profile))
        {
            SetImportStatus(Ui.Format("Import.UnsupportedProfile", profileDocument.ProfileId));
            return;
        }

        var catalogue = LoadCatalogue();
        var catalogueById = catalogue.Applications.ToDictionary(application => application.Id, StringComparer.OrdinalIgnoreCase);
        var unknownApplication = profileDocument.Applications
            .FirstOrDefault(application => !catalogueById.ContainsKey(application.ApplicationId));
        if (unknownApplication is not null)
        {
            SetImportStatus(Ui.Format("Import.UnknownApplication", unknownApplication.ApplicationId));
            return;
        }

        if (!_viewModel.Overview.HasSnapshot)
        {
            SetImportStatus(Ui["Import.CheckingPc"]);
            await _viewModel.Overview.AnalyzeAsync(_lifetimeCancellation.Token);
            if (!_viewModel.Overview.HasSnapshot)
            {
                SetImportStatus(Ui["Import.AnalysisFailed"]);
                return;
            }
        }

        _viewModel.SelectProfile(profile);
        SetImportStatus(Ui["Import.Comparing"]);
        await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
        if (!_viewModel.RecommendationsStep.HasRecommendations)
        {
            SetImportStatus(Ui["Import.CompareFailed"]);
            return;
        }

        var generatedRows = _viewModel.RecommendationsStep.Recommendations.ToDictionary(row => row.ApplicationId, StringComparer.OrdinalIgnoreCase);
        var desiredRows = new List<RecommendationRowViewModel>(profileDocument.Applications.Count);
        foreach (var desiredApplication in profileDocument.Applications)
        {
            if (!generatedRows.TryGetValue(desiredApplication.ApplicationId, out var row))
            {
                SetImportStatus(Ui.Format("Import.NotAvailableForProfile", catalogueById[desiredApplication.ApplicationId].Name, profileDocument.ProfileId));
                return;
            }

            if (row.Disposition is not (RecommendationDisposition.Recommended or RecommendationDisposition.AlreadyInstalled))
            {
                SetImportStatus(Ui.Format("Import.CannotApply", row.Name, row.Status));
                return;
            }

            if (row.Disposition == RecommendationDisposition.Recommended &&
                catalogueById[row.ApplicationId].WindowsPackage is null)
            {
                SetImportStatus(Ui.Format("Import.NoMapping", row.Name));
                return;
            }

            desiredRows.Add(row);
        }

        var desiredIds = profileDocument.Applications
            .Select(application => application.ApplicationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _viewModel.RecommendationsStep.Recommendations
                     .Where(row => !desiredIds.Contains(row.ApplicationId))
                     .ToArray())
        {
            _viewModel.RecommendationsStep.Recommendations.Remove(row);
        }

        foreach (var row in desiredRows.Where(row => row.CanSelect))
        {
            if (row.IsSelected)
            {
                row.IsSelected = false;
            }
            row.IsSelected = true;
        }

        if (_viewModel.RecommendationsStep.SelectedCount == 0)
        {
            SetImportStatus(Ui.Plural("Import.AlreadySatisfied", profileDocument.Applications.Count));
            return;
        }

        if (!_viewModel.PrepareReview())
        {
            SetImportStatus(Ui["Import.NoChanges"]);
            return;
        }

        SetImportStatus(Ui.Plural("Import.Imported", profileDocument.Applications.Count));
        _viewModel.ShowStep(AppStep.Review);
    }

    private async void HistoryView_OnExportSetupRequested(object? sender, EventArgs e)
    {
        var selected = _localExperience.SelectedHistory?.Entry;
        if (selected is null || !TryParseProfileId(selected.Profile, out var profile))
        {
            return;
        }

        var applications = selected.Applications
            .Select(application => new SetupProfileApplication(application.ApplicationId))
            .DistinctBy(application => application.ApplicationId, StringComparer.OrdinalIgnoreCase);
        await SaveSetupProfileAsync(
            ToProfileId(profile),
            applications,
            Ui.Format("Setup.DisplayName", selected.Profile));
    }

    private async Task SaveSetupProfileAsync(
        string profileId,
        IEnumerable<SetupProfileApplication> applications,
        string displayName)
    {
        if (!StorageProvider.CanSave)
        {
            return;
        }

        var portableApplications = applications
            .Where(application => !string.IsNullOrWhiteSpace(application.ApplicationId))
            .DistinctBy(application => application.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (portableApplications.Length == 0)
        {
            return;
        }

        var document = new SetupProfileDocument(
            SetupProfileDocument.CurrentKind,
            SetupProfileDocument.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            profileId,
            portableApplications,
            new SetupProfileMetadata(displayName, "0.1.0-alpha"));
        var json = _setupProfileSerializer.Serialize(document);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Ui["Export.DialogTitle"],
            SuggestedFileName = $"AgenStart-{profileId}-{DateTime.Now:yyyyMMdd}.agenstart.json",
            DefaultExtension = "json",
            FileTypeChoices = [SetupProfileFileType],
            ShowOverwritePrompt = true
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(json.AsMemory(), _lifetimeCancellation.Token);
        await writer.FlushAsync(_lifetimeCancellation.Token);
    }

    private SoftwareCatalogue LoadCatalogue()
    {
        var cataloguePath = Path.Combine(AppContext.BaseDirectory, "Data", "catalogue.json");
        using var catalogueStream = File.OpenRead(cataloguePath);
        return _catalogueLoader.Load(catalogueStream);
    }

    private static bool TryParseProfileId(string? profileId, out UserProfile profile)
    {
        switch (profileId?.Trim().ToLowerInvariant())
        {
            case "personal":
                profile = UserProfile.Personal;
                return true;
            case "development":
                profile = UserProfile.Development;
                return true;
            case "business":
                profile = UserProfile.Business;
                return true;
            case "creation":
                profile = UserProfile.Creation;
                return true;
            case "training":
                profile = UserProfile.Training;
                return true;
            default:
                profile = default;
                return false;
        }
    }

    private static string ToProfileId(UserProfile profile) => profile.ToString().ToLowerInvariant();

    private static ILocalizer Ui => AvaloniaLocalizationService.Shared;

    private void SetImportStatus(string message) => OverviewControl.SetImportStatus(message);

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportViewModel.HasReport))
        {
            ReportControl.SetExportEnabled(_viewModel.Report.HasReport && StorageProvider.CanSave);
        }

        if (e.PropertyName is nameof(RecommendationsViewModel.HasRecommendations)
            or nameof(MainWindowViewModel.CurrentStep))
        {
            UpdateRecommendationStickyHeader();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentStep))
        {
            // Each step opens at its top, not at the previous step's scroll position.
            MainScrollViewer.Offset = default;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel.Review.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel.Installation.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel.Report.PropertyChanged -= ViewModel_OnPropertyChanged;
        _viewModel.RecommendationsStep.PropertyChanged -= ViewModel_OnPropertyChanged;
        HistoryControl.ExportSetupRequested -= HistoryView_OnExportSetupRequested;
        _onboarding.Completed -= Onboarding_OnCompleted;
        OverviewControl.AnalyzeRequested -= OverviewView_OnAnalyzeRequested;
        OverviewControl.ImportSetupRequested -= OverviewView_OnImportSetupRequested;
        ReportControl.FinishRequested -= ReportView_OnFinishRequested;
        ReportControl.ExportSetupRequested -= ReportView_OnExportSetupRequested;
        InstallationControl.CancelRequested -= InstallationView_OnCancelRequested;
        InstallationControl.ViewReportRequested -= InstallationView_OnViewReportRequested;
        InstallationControl.RetryRequested -= InstallationView_OnRetryRequested;
        ReviewControl.StartInstallationRequested -= ReviewView_OnStartInstallationRequested;
        ReviewControl.BackRequested -= ReviewView_OnBackRequested;
        ReviewControl.RemoveRequested -= ReviewView_OnRemoveRequested;
        YourPcControl.ContinueToProfileRequested -= YourPcView_OnContinueToProfileRequested;
        YourPcControl.RefreshRequested -= YourPcView_OnRefreshRequested;
        RecommendationsControl.ReviewRequested -= RecommendationsView_OnReviewRequested;
        RecommendationsControl.SelectEssentialsRequested -= RecommendationsView_OnSelectEssentialsRequested;
        Opened -= OnOpened;
        _multiProfileProgressSubscription?.Dispose();
        _multiProfileProgressSubscription = null;
        _lifetimeCancellation.Cancel();
        _viewModel.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
