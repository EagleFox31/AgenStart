using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;

namespace AgenStart.Desktop.Icons;

/// <summary>
/// Resolves application artwork without coupling icon loading to the visual tree.
/// Resolution order is intentionally local-first: disk cache, packaged assets, fallback.
/// A missing or corrupt icon is always treated as a cosmetic failure.
/// </summary>
public sealed class AppIconService
{
    private static readonly Uri AssetBaseUri = new("avares://AgenStart.Desktop/");

    private static readonly IReadOnlyDictionary<string, string> PackagedAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["7zip"] = "avares://AgenStart.Desktop/Assets/AppLogos/7zip.svg",
            ["anki"] = "avares://AgenStart.Desktop/Assets/AppLogos/anki.svg",
            ["bitwarden"] = "avares://AgenStart.Desktop/Assets/AppLogos/bitwarden.svg",
            ["copyq"] = "avares://AgenStart.Desktop/Assets/AppLogos/copyq.svg",
            ["dbeaver"] = "avares://AgenStart.Desktop/Assets/AppLogos/dbeaver.svg",
            ["discord"] = "avares://AgenStart.Desktop/Assets/AppLogos/discord.svg",
            ["docker-desktop"] = "avares://AgenStart.Desktop/Assets/AppLogos/docker.svg",
            ["firefox"] = "avares://AgenStart.Desktop/Assets/AppLogos/firefox-browser.svg",
            ["flow-launcher"] = "avares://AgenStart.Desktop/Assets/AppLogos/flow-launcher.svg",
            ["git"] = "avares://AgenStart.Desktop/Assets/AppLogos/git.svg",
            ["github-cli"] = "avares://AgenStart.Desktop/Assets/AppLogos/github-cli.svg",
            ["localsend"] = "avares://AgenStart.Desktop/Assets/AppLogos/localsend.svg",
            ["obs-studio"] = "avares://AgenStart.Desktop/Assets/AppLogos/obs-studio.svg",
            ["obsidian"] = "avares://AgenStart.Desktop/Assets/AppLogos/obsidian.svg",
            ["postman"] = "avares://AgenStart.Desktop/Assets/AppLogos/postman.svg",
            ["powershell"] = "avares://AgenStart.Desktop/Assets/AppLogos/powershell.svg",
            ["powertoys"] = "avares://AgenStart.Desktop/Assets/AppLogos/microsoft-powertoys.svg",
            ["quicklook"] = "avares://AgenStart.Desktop/Assets/AppLogos/quicklook.svg",
            ["sharex"] = "avares://AgenStart.Desktop/Assets/AppLogos/sharex.svg",
            ["steam"] = "avares://AgenStart.Desktop/Assets/AppLogos/steam.svg",
            ["visual-studio-code"] = "avares://AgenStart.Desktop/Assets/AppLogos/visual-studio-code.svg",
            ["vlc"] = "avares://AgenStart.Desktop/Assets/AppLogos/vlc-media-player.svg",
            ["windows-terminal"] = "avares://AgenStart.Desktop/Assets/AppLogos/windows-terminal.svg",
            ["zoom"] = "avares://AgenStart.Desktop/Assets/AppLogos/zoom.svg",
            ["zotero"] = "avares://AgenStart.Desktop/Assets/AppLogos/zotero.svg"
        };

    private readonly object _gate = new();
    private readonly Dictionary<string, IImage?> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheDirectory;

    public AppIconService(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgenStart",
            "Cache",
            "Icons");
    }

    public static AppIconService Shared { get; } = new();

    public string CacheDirectory => _cacheDirectory;

    public IImage? Resolve(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return null;
        }

        lock (_gate)
        {
            if (_memoryCache.TryGetValue(applicationId, out var cached))
            {
                return cached;
            }
        }

        var resolved = ResolveCore(applicationId);

        lock (_gate)
        {
            _memoryCache[applicationId] = resolved;
        }

        return resolved;
    }

    public string GetPngCachePath(string applicationId) =>
        Path.Combine(_cacheDirectory, $"{SanitizeFileName(applicationId)}.png");

    private IImage? ResolveCore(string applicationId)
    {
        var cachedPng = GetPngCachePath(applicationId);
        if (File.Exists(cachedPng))
        {
            try
            {
                return new Bitmap(cachedPng);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Cached icon load failed for {applicationId}: {exception.Message}");
            }
        }

        if (!PackagedAssets.TryGetValue(applicationId, out var assetPath))
        {
            return null;
        }

        try
        {
            var source = SvgSource.Load(assetPath, AssetBaseUri);
            return source is null ? null : new SvgImage { Source = source };
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Packaged icon load failed for {applicationId}: {exception.Message}");
            return null;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }
}
