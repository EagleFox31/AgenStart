using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AgenStart.Application.Installation;
using AgenStart.Core.Catalogue;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly MainWindowViewModel _viewModel;
    private readonly LocalExperienceViewModel _localExperience = new();
    private HistoryView? _historyView;
    private SettingsView? _settingsView;
    private Button? _historyButton;
    private Button? _settingsButton;
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
        if (_historyButton is not null)
        {
            _historyButton.Click -= HistoryButton_OnClick;
        }
        if (_settingsButton is not null)
        {
            _settingsButton.Click -= SettingsButton_OnClick;
        }
        Opened -= OnOpened;
        _lifetimeCancellation.Cancel();
        _viewModel.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
