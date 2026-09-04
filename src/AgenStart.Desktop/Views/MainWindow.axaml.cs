using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Core.Catalogue;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly MainWindowViewModel _viewModel;

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

        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Closed += OnClosed;
    }

    private void OverviewButton_OnClick(object? sender, RoutedEventArgs e) => ShowOverview();
    private void YourPcButton_OnClick(object? sender, RoutedEventArgs e) => ShowYourPc();
    private void UsageProfileButton_OnClick(object? sender, RoutedEventArgs e) => ShowUsageProfile();
    private void RecommendationsButton_OnClick(object? sender, RoutedEventArgs e) => ShowRecommendations();
    private void ReviewButton_OnClick(object? sender, RoutedEventArgs e) => ShowReview();
    private void InstallationButton_OnClick(object? sender, RoutedEventArgs e) => ShowInstallation();
    private void ReportButton_OnClick(object? sender, RoutedEventArgs e) => ShowReport();

    private async void AnalyzeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasSnapshot)
        {
            YourPcButton.IsEnabled = true;
            UsageProfileButton.IsEnabled = true;
            RecommendationsButton.IsEnabled = false;
            ShowYourPc();
        }
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasSnapshot)
        {
            UsageProfileButton.IsEnabled = true;
            RecommendationsButton.IsEnabled = false;
        }
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

        InstallationButton.IsEnabled = true;
        ShowInstallation();
        await _viewModel.StartInstallationAsync(_lifetimeCancellation.Token);

        if (_viewModel.HasReport)
        {
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
            ReportButton.IsEnabled = true;
            if (_viewModel.InstallationFailedCount == 0)
            {
                ShowReport();
            }
        }
    }

    private void ViewReportButton_OnClick(object? sender, RoutedEventArgs e) => ShowReport();
    private void FinishButton_OnClick(object? sender, RoutedEventArgs e) => ShowOverview();

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
        OverviewPanel.IsVisible = ReferenceEquals(panel, OverviewPanel);
        YourPcPanel.IsVisible = ReferenceEquals(panel, YourPcPanel);
        UsageProfilePanel.IsVisible = ReferenceEquals(panel, UsageProfilePanel);
        RecommendationsPanel.IsVisible = ReferenceEquals(panel, RecommendationsPanel);
        ReviewPanel.IsVisible = ReferenceEquals(panel, ReviewPanel);
        InstallationPanel.IsVisible = ReferenceEquals(panel, InstallationPanel);
        ReportPanel.IsVisible = ReferenceEquals(panel, ReportPanel);

        OverviewStripe.IsVisible = ReferenceEquals(activeStripe, OverviewStripe);
        YourPcStripe.IsVisible = ReferenceEquals(activeStripe, YourPcStripe);
        UsageProfileStripe.IsVisible = ReferenceEquals(activeStripe, UsageProfileStripe);
        RecommendationsStripe.IsVisible = ReferenceEquals(activeStripe, RecommendationsStripe);
        ReviewStripe.IsVisible = ReferenceEquals(activeStripe, ReviewStripe);
        InstallationStripe.IsVisible = ReferenceEquals(activeStripe, InstallationStripe);
        ReportStripe.IsVisible = ReferenceEquals(activeStripe, ReportStripe);

        SetActive(OverviewButton, ReferenceEquals(activeButton, OverviewButton));
        SetActive(YourPcButton, ReferenceEquals(activeButton, YourPcButton));
        SetActive(UsageProfileButton, ReferenceEquals(activeButton, UsageProfileButton));
        SetActive(RecommendationsButton, ReferenceEquals(activeButton, RecommendationsButton));
        SetActive(ReviewButton, ReferenceEquals(activeButton, ReviewButton));
        SetActive(InstallationButton, ReferenceEquals(activeButton, InstallationButton));
        SetActive(ReportButton, ReferenceEquals(activeButton, ReportButton));
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
        _lifetimeCancellation.Cancel();
        _viewModel.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
