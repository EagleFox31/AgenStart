using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using AgenStart.Application.Installation;
using AgenStart.Application.Profiles;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
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
    private Button? _importSetupButton;
    private Button? _exportCurrentSetupButton;
    private TextBlock? _importStatusText;
    private Guid? _currentHistoryEntryId;

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
        InstallSetupProfileActions();

        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OverviewButton_OnClick(object? sender, RoutedEventArgs e) => ShowOverview();
    private void YourPcButton_OnClick(object? sender, RoutedEventArgs e) => ShowYourPc();
    private void UsageProfileButton_OnClick(object? sender, RoutedEventArgs e) => ShowUsageProfile();
    private void RecommendationsButton_OnClick(object? sender, RoutedEventArgs e) => ShowRecommendations();
    private void ReviewButton_OnClick(object? sender, RoutedEventArgs e) => ShowReview();
    private void InstallationButton_OnClick(object? sender, RoutedEventArgs e) => ShowInstallation();
    private void ReportButton_OnClick(object? sender, RoutedEventArgs e) => ShowReport();

    private async void OnOpened(object? sender, EventArgs e)
    {
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

    private async void AnalyzeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasSnapshot)
        {
            EnablePostAnalysisNavigation();
            ShowYourPc();
        }
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
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

    private void ContinueToProfile_OnClick(object? sender, RoutedEventArgs e) => ShowUsageProfile();
    private void ProfileBackButton_OnClick(object? sender, RoutedEventArgs e) => ShowYourPc();

    private void PersonalProfile_OnChecked(object? sender, RoutedEventArgs e) => SelectProfile(UserProfile.Personal);
    private void DevelopmentProfile_OnChecked(object? sender, RoutedEventArgs e) => SelectProfile(UserProfile.Development);
    private void BusinessProfile_OnChecked(object? sender, RoutedEventArgs e) => SelectProfile(UserProfile.Business);
    private void CreationProfile_OnChecked(object? sender, RoutedEventArgs e) => SelectProfile(UserProfile.Creation);
    private void TrainingProfile_OnChecked(object? sender, RoutedEventArgs e) => SelectProfile(UserProfile.Training);

    private void SelectProfile(UserProfile profile)
    {
        _viewModel.SelectProfile(profile);
        RecommendationsButton.IsEnabled = false;
    }

    private async void BuildRecommendationsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.BuildRecommendationsAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasRecommendations)
        {
            RecommendationsButton.IsEnabled = true;
            ShowRecommendations();
        }
    }

    private void SelectEssentialsButton_OnClick(object? sender, RoutedEventArgs e) =>
        _viewModel.SelectEssentialsOnly();

    private void ReviewSetupButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.PrepareReview())
        {
            ShowReview();
        }
    }

    private void ReviewBackButton_OnClick(object? sender, RoutedEventArgs e) => ShowRecommendations();

    private void RemoveReviewItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ReviewRowViewModel row })
        {
            return;
        }

        _viewModel.RemoveFromReview(row.ApplicationId);
        if (!_viewModel.HasReview)
        {
            ShowRecommendations();
        }
    }

    private async void StartInstallationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartInstallation)
        {
            return;
        }

        _currentHistoryEntryId = Guid.NewGuid();
        InstallationButton.IsEnabled = true;
        ShowInstallation();
        await _viewModel.StartInstallationAsync(_lifetimeCancellation.Token);

        if (_viewModel.HasReport)
        {
            await PersistCurrentReportAsync();
            ReportButton.IsEnabled = true;
            if (_viewModel.InstallationFailedCount == 0)
            {
                ShowReport();
            }
        }
    }

    private void CancelInstallationButton_OnClick(object? sender, RoutedEventArgs e) =>
        _viewModel.CancelInstallation();

    private async void RetryInstallationItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: InstallationRowViewModel row })
        {
            return;
        }

        await _viewModel.RetryInstallationAsync(row.ApplicationId, _lifetimeCancellation.Token);
        if (_viewModel.HasReport)
        {
            _currentHistoryEntryId ??= Guid.NewGuid();
            await PersistCurrentReportAsync();
            ReportButton.IsEnabled = true;
            if (_viewModel.InstallationFailedCount == 0)
            {
                ShowReport();
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

    private void ViewReportButton_OnClick(object? sender, RoutedEventArgs e) => ShowReport();
    private void FinishButton_OnClick(object? sender, RoutedEventArgs e) => ShowOverview();

    private void InstallUtilityViews()
    {
        if (OverviewPanel.Parent is not Grid pageHost)
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

    private void InstallSetupProfileActions()
    {
        _importSetupButton = FindButtonByContent("Import a setup");
        if (_importSetupButton is not null)
        {
            _importSetupButton.IsEnabled = StorageProvider.CanOpen;
            _importSetupButton.Click += ImportSetupButton_OnClick;

            if (_importSetupButton.Parent?.Parent is StackPanel overviewColumn)
            {
                _importStatusText = new TextBlock
                {
                    IsVisible = false,
                    FontSize = 13,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                _importStatusText.Classes.Add("subtitle");
                overviewColumn.Children.Add(_importStatusText);
            }
        }

        _exportCurrentSetupButton = FindButtonByContent("Export this setup");
        if (_exportCurrentSetupButton is not null)
        {
            _exportCurrentSetupButton.IsEnabled = false;
            _exportCurrentSetupButton.Click += ExportCurrentSetupButton_OnClick;
        }
    }

    private Button? FindNavigationButton(string label) =>
        this.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Any(text => string.Equals(text.Text, label, StringComparison.Ordinal)));

    private Button? FindButtonByContent(string content) =>
        this.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content as string, content, StringComparison.Ordinal));

    private void HistoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_historyView is not null && _historyButton is not null)
        {
            ShowUtilityPage(_historyView, _historyButton);
        }
    }

    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_settingsView is not null && _settingsButton is not null)
        {
            ShowUtilityPage(_settingsView, _settingsButton);
        }
    }

    private async void ImportSetupButton_OnClick(object? sender, RoutedEventArgs e)
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
        ShowReview();
    }

    private async void ExportCurrentSetupButton_OnClick(object? sender, RoutedEventArgs e)
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

    private void SetImportStatus(string message)
    {
        if (_importStatusText is null)
        {
            return;
        }

        _importStatusText.Text = message;
        _importStatusText.IsVisible = true;
    }

    private void ShowOverview() => ShowPage(OverviewPanel, OverviewButton, OverviewStripe);

    private void ShowYourPc()
    {
        if (YourPcButton.IsEnabled)
        {
            ShowPage(YourPcPanel, YourPcButton, YourPcStripe);
        }
    }

    private void ShowUsageProfile()
    {
        if (UsageProfileButton.IsEnabled)
        {
            ShowPage(UsageProfilePanel, UsageProfileButton, UsageProfileStripe);
        }
    }

    private void ShowRecommendations()
    {
        if (RecommendationsButton.IsEnabled)
        {
            ShowPage(RecommendationsPanel, RecommendationsButton, RecommendationsStripe);
        }
    }

    private void ShowReview()
    {
        if (_viewModel.HasReview)
        {
            ShowPage(ReviewPanel, ReviewButton, ReviewStripe);
        }
    }

    private void ShowInstallation()
    {
        if (InstallationButton.IsEnabled)
        {
            ShowPage(InstallationPanel, InstallationButton, InstallationStripe);
        }
    }

    private void ShowReport()
    {
        if (_viewModel.HasReport)
        {
            ShowPage(ReportPanel, ReportButton, ReportStripe);
        }
    }

    private void ShowPage(Control panel, Button activeButton, Border activeStripe)
    {
        HideAllPages();
        panel.IsVisible = true;
        activeStripe.IsVisible = true;
        SetActive(activeButton, true);
    }

    private void ShowUtilityPage(Control panel, Button activeButton)
    {
        HideAllPages();
        panel.IsVisible = true;
        SetActive(activeButton, true);
    }

    private void HideAllPages()
    {
        OverviewPanel.IsVisible = false;
        YourPcPanel.IsVisible = false;
        UsageProfilePanel.IsVisible = false;
        RecommendationsPanel.IsVisible = false;
        ReviewPanel.IsVisible = false;
        InstallationPanel.IsVisible = false;
        ReportPanel.IsVisible = false;
        if (_historyView is not null)
        {
            _historyView.IsVisible = false;
        }
        if (_settingsView is not null)
        {
            _settingsView.IsVisible = false;
        }

        OverviewStripe.IsVisible = false;
        YourPcStripe.IsVisible = false;
        UsageProfileStripe.IsVisible = false;
        RecommendationsStripe.IsVisible = false;
        ReviewStripe.IsVisible = false;
        InstallationStripe.IsVisible = false;
        ReportStripe.IsVisible = false;

        SetActive(OverviewButton, false);
        SetActive(YourPcButton, false);
        SetActive(UsageProfileButton, false);
        SetActive(RecommendationsButton, false);
        SetActive(ReviewButton, false);
        SetActive(InstallationButton, false);
        SetActive(ReportButton, false);
        if (_historyButton is not null)
        {
            SetActive(_historyButton, false);
        }
        if (_settingsButton is not null)
        {
            SetActive(_settingsButton, false);
        }
    }

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
            if (_exportCurrentSetupButton is not null)
            {
                _exportCurrentSetupButton.IsEnabled = _viewModel.HasReport && StorageProvider.CanSave;
            }
        }
    }

    private static void SetActive(Button button, bool isActive)
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
        if (_importSetupButton is not null)
        {
            _importSetupButton.Click -= ImportSetupButton_OnClick;
        }
        if (_exportCurrentSetupButton is not null)
        {
            _exportCurrentSetupButton.Click -= ExportCurrentSetupButton_OnClick;
        }
        Opened -= OnOpened;
        _lifetimeCancellation.Cancel();
        _viewModel.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
