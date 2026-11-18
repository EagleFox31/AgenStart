using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Desktop.ViewModels.Steps;

namespace AgenStart.Desktop.Views;

public sealed partial class RecommendationsView : UserControl
{
    public RecommendationsView()
    {
        InitializeComponent();
    }

    public event EventHandler? ReviewRequested;
    public event EventHandler? SelectEssentialsRequested;

    private void ReviewButton_OnClick(object? sender, RoutedEventArgs e) =>
        ReviewRequested?.Invoke(this, EventArgs.Empty);

    private void SelectEssentialsButton_OnClick(object? sender, RoutedEventArgs e) =>
        SelectEssentialsRequested?.Invoke(this, EventArgs.Empty);

    private void ListLayoutButton_OnClick(object? sender, RoutedEventArgs e) => SetCardLayout(false);

    private void CardLayoutButton_OnClick(object? sender, RoutedEventArgs e) => SetCardLayout(true);

    private void SetCardLayout(bool isCardLayout)
    {
        if (DataContext is RecommendationsViewModel viewModel)
        {
            viewModel.IsCardLayout = isCardLayout;
        }
    }
}
