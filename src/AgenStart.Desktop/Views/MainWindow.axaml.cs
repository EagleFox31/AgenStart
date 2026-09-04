using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        YourPcButton.IsEnabled = false;
        Closed += OnClosed;
    }

    private void OverviewButton_OnClick(object? sender, RoutedEventArgs e) => ShowOverview();

    private void YourPcButton_OnClick(object? sender, RoutedEventArgs e) => ShowYourPc();

    private async void AnalyzeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);
        if (_viewModel.HasSnapshot)
        {
            YourPcButton.IsEnabled = true;
            ShowYourPc();
        }
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.AnalyzeAsync(_lifetimeCancellation.Token);

    private void ShowOverview()
    {
        OverviewPanel.IsVisible = true;
        YourPcPanel.IsVisible = false;
        OverviewStripe.IsVisible = true;
        YourPcStripe.IsVisible = false;
        SetActive(OverviewButton, true);
        SetActive(YourPcButton, false);
    }

    private void ShowYourPc()
    {
        if (!YourPcButton.IsEnabled)
        {
            return;
        }

        OverviewPanel.IsVisible = false;
        YourPcPanel.IsVisible = true;
        OverviewStripe.IsVisible = false;
        YourPcStripe.IsVisible = true;
        SetActive(OverviewButton, false);
        SetActive(YourPcButton, true);
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
