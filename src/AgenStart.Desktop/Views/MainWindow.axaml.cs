using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AgenStart.Application.Installation;
using AgenStart.Application.Profiles;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.ViewModels;
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
    private readonly LocalExperienceViewModel _localExperience = new();
    private readonly SetupProfileSerializer _setupProfileSerializer = new();
    private readonly SoftwareCatalogueLoader _catalogueLoader = new();
    private HistoryView? _historyView;
    private SettingsView? _settingsView;
    private Button? _historyButton;
    private Button? _settingsButton;
    private Guid? _currentHistoryEntryId;
    private IDisposable? _multiProfileProgressSubscription;

    public MainWindow()
    {
        _viewModel = new MainWindowViewModel();

        InitializeComponent();
        DataContext = _viewModel;

        YourPcButton.IsEnabled = false;
        UsageProfileButton.IsEnabled = false;
        RecommendationsButton.IsEnabled = false;
        ReviewButton.IsEnabled = false;
        InstallationButton.IsEnabled = false;
        ReportButton.IsEnabled = false;

        InstallUtilityViews();

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
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void NavButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<AppStep>(tag, ignoreCase: false, out var step))
        {
            _viewModel.ShowStep(step);
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        InstallMultiProfileExperience();

        try
        {
            await _localExperience.InitializeAsync(_lifetimeCancellation.Token);
            if (_localExperience.AnalyzeOnStartup && !_viewModel.HasSnapshot)
            {
                await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
                if (_viewModel.HasSnapshot)
                {
                    EnablePostAnalysisNavigation();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private void InstallMultiProfileExperience()
    {
        UsageProfilesControl.SetSelection(_viewModel.SelectedProfile);

        UsageProfilesControl.SelectionChanged += profiles =>
        {
            if (profiles == UserProfile.None)
            {
                return;
            }

            _viewModel.SelectProfile(profiles);
            RecommendationsButton.IsEnabled = false;
        };

        UsageProfilesControl.BuildRequested += async (_, _) =>
        {
            UsageProfilesControl.BeginRecommendationBuild();
            _viewModel.SelectProfile(UsageProfilesControl.SelectedProfiles);

            await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
            if (_viewModel.HasRecommendations)
            {
                UsageProfilesControl.CompleteRecommendationBuild();
                RecommendationsButton.IsEnabled = true;
                _viewModel.ShowStep(AppStep.Recommendations);
                UpdateRecommendationStickyHeader();
            }
            else
            {
                UsageProfilesControl.FailRecommendationBuild(_viewModel.RecommendationStatus);
            }
        };

        UsageProfilesControl.BackRequested += (_, _) => _viewModel.ShowStep(AppStep.YourPc);

        _multiProfileProgressSubscription = RecommendationPipelineDiagnostics.Subscribe(stage =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel.IsBusy && UsageProfilesControl.IsVisible)
                {
                    UsageProfilesControl.UpdateRecommendationProgress(stage);
                }
            }));
    }

    private async void OverviewView_OnAnalyzeRequested(object? sender, EventArgs e)
    {
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasSnapshot)
        {
            EnablePostAnalysisNavigation();
            _viewModel.ShowStep(AppStep.YourPc);
        }
    }

    private async void YourPcView_OnRefreshRequested(object? sender, EventArgs e)
    {
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasSnapshot)
        {
            EnablePostAnalysisNavigation();
        }
    }

    private void EnablePostAnalysisNavigation()
    {
        YourPcButton.IsEnabled = true;
        UsageProfileButton.IsEnabled = true;
        RecommendationsButton.IsEnabled = false;
    }

    private void YourPcView_OnContinueToProfileRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.UsageProfile);

    private void RecommendationsView_OnSelectEssentialsRequested(object? sender, EventArgs e) =>
        _viewModel.SelectEssentialsOnly();

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
            _viewModel.HasRecommendations &&
            MainScrollViewer.Offset.Y >= 118;
    }

    private void ReviewView_OnBackRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.Recommendations);

    private void ReviewView_OnRemoveRequested(object? sender, ReviewRowViewModel row)
    {
        _viewModel.RemoveFromReview(row.ApplicationId);
        if (!_viewModel.HasReview)
        {
            _viewModel.ShowStep(AppStep.Recommendations);
        }
    }

    private async void ReviewView_OnStartInstallationRequested(object? sender, EventArgs e)
    {
        if (!_viewModel.CanStartInstallation)
        {
            return;
        }

        _currentHistoryEntryId = Guid.NewGuid();
        InstallationButton.IsEnabled = true;
        _viewModel.ShowStep(AppStep.Installation);
        await _viewModel.StartInstallationAsync(_lifetimeCancellation.Token);

        if (_viewModel.HasReport)
        {
            await PersistCurrentReportAsync();
            ReportButton.IsEnabled = true;
            if (_viewModel.InstallationFailedCount == 0)
            {
                _viewModel.ShowStep(AppStep.Report);
            }
        }
    }

    private void InstallationView_OnCancelRequested(object? sender, EventArgs e) =>
        _viewModel.CancelInstallation();

    private void InstallationView_OnViewReportRequested(object? sender, EventArgs e) =>
        _viewModel.ShowStep(AppStep.Report);

    private async void InstallationView_OnRetryRequested(object? sender, InstallationRowViewModel row)
    {
        await _viewModel.RetryInstallationAsync(row.ApplicationId, _lifetimeCancellation.Token);
        if (_viewModel.HasReport)
        {
            _currentHistoryEntryId ??= Guid.NewGuid();
            await PersistCurrentReportAsync();
            ReportButton.IsEnabled = true;
            if (_viewModel.InstallationFailedCount == 0)
            {
                _viewModel.ShowStep(AppStep.Report);
            }
        }
    }

    private async Task PersistCurrentReportAsync()
    {
        if (!_viewModel.HasReport || _currentHistoryEntryId is null)
        {
            return;
        }

        var applications = _viewModel.ReportItems.Select(row => new SetupHistoryApplication(
            row.ApplicationId,
            row.Name,
            row.Result,
            row.InstalledVersion));
        var skippedCount = _viewModel.InstallationItems.Count(row => row.State == InstallationQueueItemState.Skipped);
        var cancelledCount = _viewModel.InstallationItems.Count(row => row.State == InstallationQueueItemState.Cancelled);

        try
        {
            await _localExperience.RecordSessionAsync(
                _currentHistoryEntryId.Value,
                _viewModel.SelectedProfileName,
                _viewModel.ReportProcessedCount,
                _viewModel.ReportInstalledCount,
                _viewModel.ReportAlreadyInstalledCount,
                _viewModel.ReportFailedCount,
                skippedCount,
                cancelledCount,
                _viewModel.ReportRequiresReboot,
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
        if (!_viewModel.HasReport || _viewModel.ReviewItems.Count == 0)
        {
            return;
        }

        var applications = _viewModel.ReviewItems.Select(row =>
            new SetupProfileApplication(row.ApplicationId, row.Reason));
        await SaveSetupProfileAsync(
            ToProfileId(_viewModel.SelectedProfile),
            applications,
            $"{_viewModel.SelectedProfileName} setup");
    }

    private void InstallUtilityViews()
    {
        if (OverviewControl.Parent is not Grid pageHost)
        {
            return;
        }

        _historyView = new HistoryView
        {
            DataContext = _localExperience,
            IsVisible = false
        };
        _historyView.ExportSetupRequested += HistoryView_OnExportSetupRequested;
        _settingsView = new SettingsView
        {
            DataContext = _localExperience,
            IsVisible = false
        };
        pageHost.Children.Add(_historyView);
        pageHost.Children.Add(_settingsView);

        _historyButton = FindNavigationButton("History");
        _settingsButton = FindNavigationButton("Settings");

        if (_historyButton is not null)
        {
            _historyButton.IsEnabled = true;
            _historyButton.Click += HistoryButton_OnClick;
        }

        if (_settingsButton is not null)
        {
            _settingsButton.IsEnabled = true;
            _settingsButton.Click += SettingsButton_OnClick;
        }
    }

    private Button? FindNavigationButton(string label) =>
        this.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Any(text => string.Equals(text.Text, label, StringComparison.Ordinal)));

    private void HistoryButton_OnClick(object? sender, RoutedEventArgs e) => _viewModel.ShowStep(AppStep.History);
    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e) => _viewModel.ShowStep(AppStep.Settings);

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
            SetImportStatus("AgenStart could not read the selected setup file.");
        }
        catch (UnauthorizedAccessException)
        {
            SetImportStatus("AgenStart does not have permission to read the selected setup file.");
        }
    }

    private async Task ImportSetupAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            SetImportStatus("File import is not available on this platform.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import AgenStart setup",
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
            SetImportStatus("This setup file is larger than AgenStart's 256 KB safety limit.");
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var json = await reader.ReadToEndAsync(_lifetimeCancellation.Token);
        var readResult = _setupProfileSerializer.Deserialize(json);
        if (!readResult.IsValid || readResult.Profile is null)
        {
            SetImportStatus(readResult.Errors.FirstOrDefault()?.Message ?? "This setup file is invalid.");
            return;
        }

        var profileDocument = readResult.Profile;
        if (!TryParseProfileId(profileDocument.ProfileId, out var profile))
        {
            SetImportStatus($"Profile '{profileDocument.ProfileId}' is not supported by this AgenStart build.");
            return;
        }

        var catalogue = LoadCatalogue();
        var catalogueById = catalogue.Applications.ToDictionary(application => application.Id, StringComparer.OrdinalIgnoreCase);
        var unknownApplication = profileDocument.Applications
            .FirstOrDefault(application => !catalogueById.ContainsKey(application.ApplicationId));
        if (unknownApplication is not null)
        {
            SetImportStatus($"'{unknownApplication.ApplicationId}' is not present in the current trusted AgenStart catalogue.");
            return;
        }

        if (!_viewModel.HasSnapshot)
        {
            SetImportStatus("Checking this PC locally before comparing the imported setup…");
            await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
            if (!_viewModel.HasSnapshot)
            {
                SetImportStatus("AgenStart could not analyse this PC, so the imported setup was not applied.");
                return;
            }

            EnablePostAnalysisNavigation();
        }

        _viewModel.SelectProfile(profile);
        SetImportStatus("Comparing the imported setup with this PC…");
        await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
        if (!_viewModel.HasRecommendations)
        {
            SetImportStatus("AgenStart could not compare the imported setup with the current catalogue and inventory.");
            return;
        }

        var generatedRows = _viewModel.Recommendations.ToDictionary(row => row.ApplicationId, StringComparer.OrdinalIgnoreCase);
        var desiredRows = new List<RecommendationRowViewModel>(profileDocument.Applications.Count);
        foreach (var desiredApplication in profileDocument.Applications)
        {
            if (!generatedRows.TryGetValue(desiredApplication.ApplicationId, out var row))
            {
                SetImportStatus($"{catalogueById[desiredApplication.ApplicationId].Name} is not available for the imported {profileDocument.ProfileId} profile in this catalogue.");
                return;
            }

            if (row.Disposition is not (RecommendationDisposition.Recommended or RecommendationDisposition.AlreadyInstalled))
            {
                SetImportStatus($"{row.Name} cannot be safely applied on this PC: {row.Status}.");
                return;
            }

            if (row.Disposition == RecommendationDisposition.Recommended &&
                catalogueById[row.ApplicationId].WindowsPackage is null)
            {
                SetImportStatus($"{row.Name} has no trusted Windows package mapping in the current catalogue.");
                return;
            }

            desiredRows.Add(row);
        }

        var desiredIds = profileDocument.Applications
            .Select(application => application.ApplicationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _viewModel.Recommendations
                     .Where(row => !desiredIds.Contains(row.ApplicationId))
                     .ToArray())
        {
            _viewModel.Recommendations.Remove(row);
        }

        foreach (var row in desiredRows.Where(row => row.CanSelect))
        {
            if (row.IsSelected)
            {
                row.IsSelected = false;
            }
            row.IsSelected = true;
        }

        RecommendationsButton.IsEnabled = true;
        if (_viewModel.SelectedCount == 0)
        {
            SetImportStatus($"Imported setup already satisfied: all {profileDocument.Applications.Count} application(s) are installed on this PC.");
            return;
        }

        if (!_viewModel.PrepareReview())
        {
            SetImportStatus("The imported setup produced no installable changes after comparison.");
            return;
        }

        SetImportStatus($"Imported {profileDocument.Applications.Count} application(s). Review the exact changes before installation.");
        ReviewButton.IsEnabled = true;
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
            $"{selected.Profile} setup");
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
            Title = "Export AgenStart setup",
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

    private void SetImportStatus(string message) => OverviewControl.SetImportStatus(message);

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasReview))
        {
            ReviewButton.IsEnabled = _viewModel.HasReview;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.InstallationTotalCount) or nameof(MainWindowViewModel.IsInstalling))
        {
            InstallationButton.IsEnabled = _viewModel.InstallationTotalCount > 0 || _viewModel.IsInstalling || _viewModel.HasReport;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.HasReport))
        {
            ReportButton.IsEnabled = _viewModel.HasReport;
            ReportControl.SetExportEnabled(_viewModel.HasReport && StorageProvider.CanSave);
        }

        if (e.PropertyName is nameof(MainWindowViewModel.HasRecommendations)
            or nameof(MainWindowViewModel.CurrentStep))
        {
            UpdateRecommendationStickyHeader();
        }

        if (e.PropertyName == nameof(MainWindowViewModel.CurrentStep))
        {
            if (_historyView is not null)
            {
                _historyView.IsVisible = _viewModel.IsHistory;
            }
            if (_settingsView is not null)
            {
                _settingsView.IsVisible = _viewModel.IsSettings;
            }
            if (_historyButton is not null)
            {
                SetActiveClass(_historyButton, _viewModel.IsHistory);
            }
            if (_settingsButton is not null)
            {
                SetActiveClass(_settingsButton, _viewModel.IsSettings);
            }
        }
    }

    private static void SetActiveClass(Button button, bool isActive)
    {
        const string activeClass = "active";
        if (isActive)
        {
            if (!button.Classes.Contains(activeClass))
            {
                button.Classes.Add(activeClass);
            }
        }
        else
        {
            button.Classes.Remove(activeClass);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        if (_historyView is not null)
        {
            _historyView.ExportSetupRequested -= HistoryView_OnExportSetupRequested;
        }
        if (_historyButton is not null)
        {
            _historyButton.Click -= HistoryButton_OnClick;
        }
        if (_settingsButton is not null)
        {
            _settingsButton.Click -= SettingsButton_OnClick;
        }
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
