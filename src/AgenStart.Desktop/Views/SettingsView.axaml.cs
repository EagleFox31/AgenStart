using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Desktop.LocalData;

namespace AgenStart.Desktop.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void ClearLocalDataButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LocalExperienceViewModel viewModel)
        {
            viewModel.BeginClearLocalData();
        }
    }

    private void CancelClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LocalExperienceViewModel viewModel)
        {
            viewModel.CancelClearLocalData();
        }
    }

    private async void ConfirmClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LocalExperienceViewModel viewModel)
        {
            await viewModel.ConfirmClearLocalDataAsync();
        }
    }
}
