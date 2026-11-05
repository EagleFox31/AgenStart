using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.Views;

namespace AgenStart.Desktop;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Apply the saved palette before the first window is built so it never flashes the default one.
        AvaloniaThemeService.Shared.Apply(LoadSavedTheme());
    }

    private static string LoadSavedTheme()
    {
        try
        {
            // The store uses ConfigureAwait(false) throughout, so blocking here cannot deadlock.
            return new LocalExperienceStore().LoadSettingsAsync().GetAwaiter().GetResult().Theme;
        }
        catch (Exception)
        {
            return AppThemes.Default;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
