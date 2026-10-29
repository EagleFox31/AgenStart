using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgenStart.Desktop.Views;

public sealed partial class YourPcView : UserControl
{
    public YourPcView()
    {
        InitializeComponent();
    }

    public event EventHandler? ContinueToProfileRequested;
    public event EventHandler? RefreshRequested;

    private void ContinueButton_OnClick(object? sender, RoutedEventArgs e) =>
        ContinueToProfileRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);
}
