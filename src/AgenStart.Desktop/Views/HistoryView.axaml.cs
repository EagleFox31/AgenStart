using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgenStart.Desktop.Views;

public sealed partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    public event EventHandler? ExportSetupRequested;

    private void ExportSetupButton_OnClick(object? sender, RoutedEventArgs e) =>
        ExportSetupRequested?.Invoke(this, EventArgs.Empty);
}
