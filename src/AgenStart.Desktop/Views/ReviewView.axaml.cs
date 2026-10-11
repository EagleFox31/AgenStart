using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Views;

public sealed partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
    }

    public event EventHandler? StartInstallationRequested;
    public event EventHandler? BackRequested;
    public event EventHandler<ReviewRowViewModel>? RemoveRequested;

    private void StartButton_OnClick(object? sender, RoutedEventArgs e) =>
        StartInstallationRequested?.Invoke(this, EventArgs.Empty);

    private void BackButton_OnClick(object? sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void RemoveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ReviewRowViewModel row })
        {
            RemoveRequested?.Invoke(this, row);
        }
    }
}
