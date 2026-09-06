using Avalonia;
using AgenStart.Desktop.Icons;

namespace AgenStart.Desktop;

internal static class Program
{
    private static readonly string[] IconSmokeApplicationIds =
    [
        "7zip",
        "anki",
        "bitwarden",
        "copyq",
        "dbeaver",
        "discord",
        "docker-desktop",
        "firefox",
        "flow-launcher",
        "git",
        "github-cli",
        "localsend",
        "obs-studio",
        "obsidian",
        "postman",
        "powershell",
        "powertoys",
        "quicklook",
        "sharex",
        "steam",
        "visual-studio-code",
        "vlc",
        "windows-terminal",
        "zoom",
        "zotero"
    ];

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--icon-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            return RunIconSmoke();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static int RunIconSmoke()
    {
        try
        {
            BuildAvaloniaApp().SetupWithoutStarting();

            foreach (var applicationId in IconSmokeApplicationIds)
            {
                if (AppIconService.Shared.Resolve(applicationId) is null)
                {
                    return 2;
                }
            }

            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
