using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class InstallationView : UserControl
{
    public InstallationView()
    {
        InitializeComponent();
    }

    public event EventHandler? CancelRequested;
    public event EventHandler? ViewReportRequested;
    public event EventHandler<InstallationRowViewModel>? RetryRequested;

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void ViewReportButton_OnClick(object? sender, RoutedEventArgs e) =>
        ViewReportRequested?.Invoke(this, EventArgs.Empty);

    private void RetryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InstallationRowViewModel row })
        {
            RetryRequested?.Invoke(this, row);
        }
    }
}
