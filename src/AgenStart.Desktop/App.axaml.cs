using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.Views;

namespace AgenStart.Desktop;

public sealed partial class App : Avalonia.Application
{
    private DesktopSettings _startupSettings = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Settings are read once, and palette + language applied before the first window is built,
        // so the window never flashes the default look or the wrong language.
        _startupSettings = LoadSettings();
        AvaloniaThemeService.Shared.Apply(_startupSettings.Theme);
        AvaloniaLocalizationService.Shared.Apply(
            _startupSettings.Language ?? AppLanguages.FromCulture(CultureInfo.CurrentUICulture));
    }

    private static DesktopSettings LoadSettings()
    {
        try
        {
            // The store uses ConfigureAwait(false) throughout, so blocking here cannot deadlock.
            return new LocalExperienceStore().LoadSettingsAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return new DesktopSettings();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_startupSettings);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
