using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgenStart.Desktop.Views;

public sealed partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    public event EventHandler? AnalyzeRequested;
    public event EventHandler? ImportSetupRequested;

    public void SetImportSetupEnabled(bool isEnabled) => ImportSetupButton.IsEnabled = isEnabled;

    public void SetImportStatus(string message)
    {
        ImportStatusText.Text = message;
        ImportStatusText.IsVisible = true;
    }

    private void AnalyzeButton_OnClick(object? sender, RoutedEventArgs e) =>
        AnalyzeRequested?.Invoke(this, EventArgs.Empty);

    private void ImportSetupButton_OnClick(object? sender, RoutedEventArgs e) =>
        ImportSetupRequested?.Invoke(this, EventArgs.Empty);
}
