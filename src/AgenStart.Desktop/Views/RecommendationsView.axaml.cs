using Avalonia.Controls;
using Avalonia.Interactivity;

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
}
