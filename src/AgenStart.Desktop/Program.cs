using System.Text.Json;
using Avalonia;
using AgenStart.Desktop.Icons;

namespace AgenStart.Desktop;

internal static class Program
{
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

            var cataloguePath = Path.Combine(AppContext.BaseDirectory, "Data", "catalogue.json");
            if (!File.Exists(cataloguePath))
            {
                return 3;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(cataloguePath));
            if (!document.RootElement.TryGetProperty("applications", out var applications) ||
                applications.ValueKind != JsonValueKind.Array ||
                applications.GetArrayLength() == 0)
            {
                return 4;
            }

            foreach (var application in applications.EnumerateArray())
            {
                if (!application.TryGetProperty("id", out var idElement))
                {
                    return 5;
                }

                var applicationId = idElement.GetString();
                if (string.IsNullOrWhiteSpace(applicationId))
                {
                    return 5;
                }

                if (AppIconService.Shared.Resolve(applicationId) is null)
                {
                    Console.Error.WriteLine($"Missing packaged icon for catalogue application: {applicationId}");
                    return 2;
                }
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
