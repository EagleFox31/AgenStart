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
        Closed += OnClosed;
    }

    private void OverviewButton_OnClick(object? sender, RoutedEventArgs e) => ShowOverview();

    private void YourPcButton_OnClick(object? sender, RoutedEventArgs e) => ShowYourPc();

    private void UsageProfileButton_OnClick(object? sender, RoutedEventArgs e) => ShowUsageProfile();

    private void RecommendationsButton_OnClick(object? sender, RoutedEventArgs e) => ShowRecommendations();

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

    private void PersonalProfile_OnChecked(object? sender, RoutedEventArgs e) =>
        _viewModel.SelectProfile(UserProfile.Personal);

    private void DevelopmentProfile_OnChecked(object? sender, RoutedEventArgs e) =>
        _viewModel.SelectProfile(UserProfile.Development);

    private void BusinessProfile_OnChecked(object? sender, RoutedEventArgs e) =>
        _viewModel.SelectProfile(UserProfile.Business);

    private void CreationProfile_OnChecked(object? sender, RoutedEventArgs e) =>
        _viewModel.SelectProfile(UserProfile.Creation);

    private void TrainingProfile_OnChecked(object? sender, RoutedEventArgs e) =>
        _viewModel.SelectProfile(UserProfile.Training);

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

    private void ShowOverview() => ShowPage(
        OverviewPanel,
        OverviewButton,
        OverviewStripe);

    private void ShowYourPc()
    {
        if (!YourPcButton.IsEnabled)
        {
            return;
        }

        ShowPage(YourPcPanel, YourPcButton, YourPcStripe);
    }

    private void ShowUsageProfile()
    {
        if (!UsageProfileButton.IsEnabled)
        {
            return;
        }

        ShowPage(UsageProfilePanel, UsageProfileButton, UsageProfileStripe);
    }

    private void ShowRecommendations()
    {
        if (!RecommendationsButton.IsEnabled)
        {
            return;
        }

        ShowPage(RecommendationsPanel, RecommendationsButton, RecommendationsStripe);
    }

    private void ShowPage(Control panel, Button activeButton, Border activeStripe)
    {
        OverviewPanel.IsVisible = ReferenceEquals(panel, OverviewPanel);
        YourPcPanel.IsVisible = ReferenceEquals(panel, YourPcPanel);
        UsageProfilePanel.IsVisible = ReferenceEquals(panel, UsageProfilePanel);
        RecommendationsPanel.IsVisible = ReferenceEquals(panel, RecommendationsPanel);

        OverviewStripe.IsVisible = ReferenceEquals(activeStripe, OverviewStripe);
        YourPcStripe.IsVisible = ReferenceEquals(activeStripe, YourPcStripe);
        UsageProfileStripe.IsVisible = ReferenceEquals(activeStripe, UsageProfileStripe);
        RecommendationsStripe.IsVisible = ReferenceEquals(activeStripe, RecommendationsStripe);

        SetActive(OverviewButton, ReferenceEquals(activeButton, OverviewButton));
        SetActive(YourPcButton, ReferenceEquals(activeButton, YourPcButton));
        SetActive(UsageProfileButton, ReferenceEquals(activeButton, UsageProfileButton));
        SetActive(RecommendationsButton, ReferenceEquals(activeButton, RecommendationsButton));
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
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
