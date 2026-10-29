using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgenStart.Desktop.Views;

public sealed partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
    }

    public event EventHandler? FinishRequested;
    public event EventHandler? ExportSetupRequested;

    public void SetExportEnabled(bool isEnabled) => ExportSetupButton.IsEnabled = isEnabled;

    private void FinishButton_OnClick(object? sender, RoutedEventArgs e) =>
        FinishRequested?.Invoke(this, EventArgs.Empty);

    private void ExportSetupButton_OnClick(object? sender, RoutedEventArgs e) =>
        ExportSetupRequested?.Invoke(this, EventArgs.Empty);
}
