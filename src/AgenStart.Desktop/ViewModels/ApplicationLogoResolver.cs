using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgenStart.Desktop.ViewModels;

internal static class ApplicationLogoResolver
{
    private static readonly IReadOnlyDictionary<string, string> LogoResources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["termius"] = "avares://AgenStart.Desktop/Assets/ApplicationLogos/termius.png"
        };

    private static readonly Dictionary<string, Bitmap?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object CacheLock = new();

    public static Bitmap? Resolve(string applicationId)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(applicationId, out var cached))
            {
                return cached;
            }

            var logo = Load(applicationId);
            Cache[applicationId] = logo;
            return logo;
        }
    }

    private static Bitmap? Load(string applicationId)
    {
        if (!LogoResources.TryGetValue(applicationId, out var resource))
        {
            return null;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri(resource, UriKind.Absolute));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
