using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Svg.Skia;

namespace AgenStart.Desktop.Icons;

/// <summary>
/// Resolves application artwork without coupling icon loading to the visual tree.
/// Resolution order is intentionally local-first: disk cache, packaged assets, fallback.
/// A missing or corrupt icon is always treated as a cosmetic failure at runtime, while
/// the packaged icon smoke test enforces complete coverage for the curated catalogue.
/// </summary>
public sealed class AppIconService
{
    private static readonly Uri AssetBaseUri = new("avares://AgenStart.Desktop/");

    private static readonly IReadOnlyDictionary<string, string> PackagedAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["7zip"] = "avares://AgenStart.Desktop/Assets/AppLogos/7zip.svg",
            ["adobe-acrobat-reader"] = "avares://AgenStart.Desktop/Assets/AppLogos/adobe-acrobat-reader.svg",
            ["anki"] = "avares://AgenStart.Desktop/Assets/AppLogos/anki.svg",
            ["audacity"] = "avares://AgenStart.Desktop/Assets/AppLogos/audacity.svg",
            ["battle-net"] = "avares://AgenStart.Desktop/Assets/AppLogos/battle-net.svg",
            ["bitwarden"] = "avares://AgenStart.Desktop/Assets/AppLogos/bitwarden.svg",
            ["blender"] = "avares://AgenStart.Desktop/Assets/AppLogos/blender.svg",
            ["brave"] = "avares://AgenStart.Desktop/Assets/AppLogos/brave.svg",
            ["copyq"] = "avares://AgenStart.Desktop/Assets/AppLogos/copyq.svg",
            ["dbeaver"] = "avares://AgenStart.Desktop/Assets/AppLogos/dbeaver.svg",
            ["discord"] = "avares://AgenStart.Desktop/Assets/AppLogos/discord.svg",
            ["docker-desktop"] = "avares://AgenStart.Desktop/Assets/AppLogos/docker.svg",
            ["ea-app"] = "avares://AgenStart.Desktop/Assets/AppLogos/ea-app.svg",
            ["epic-games"] = "avares://AgenStart.Desktop/Assets/AppLogos/epic-games.svg",
            ["everything"] = "avares://AgenStart.Desktop/Assets/AppLogos/everything.svg",
            ["firefox"] = "avares://AgenStart.Desktop/Assets/AppLogos/firefox-browser.svg",
            ["flow-launcher"] = "avares://AgenStart.Desktop/Assets/AppLogos/flow-launcher.svg",
            ["gimp"] = "avares://AgenStart.Desktop/Assets/AppLogos/gimp.svg",
            ["git"] = "avares://AgenStart.Desktop/Assets/AppLogos/git.svg",
            ["github-cli"] = "avares://AgenStart.Desktop/Assets/AppLogos/github-cli.svg",
            ["gog-galaxy"] = "avares://AgenStart.Desktop/Assets/AppLogos/gog-galaxy.svg",
            ["google-chrome"] = "avares://AgenStart.Desktop/Assets/AppLogos/google-chrome.svg",
            ["handbrake"] = "avares://AgenStart.Desktop/Assets/AppLogos/handbrake.svg",
            ["hwinfo"] = "avares://AgenStart.Desktop/Assets/AppLogos/hwinfo.png",
            ["inkscape"] = "avares://AgenStart.Desktop/Assets/AppLogos/inkscape.svg",
            ["kdenlive"] = "avares://AgenStart.Desktop/Assets/AppLogos/kdenlive.svg",
            ["kodi"] = "avares://AgenStart.Desktop/Assets/AppLogos/kodi.svg",
            ["krita"] = "avares://AgenStart.Desktop/Assets/AppLogos/krita.svg",
            ["libreoffice"] = "avares://AgenStart.Desktop/Assets/AppLogos/libreoffice.svg",
            ["localsend"] = "avares://AgenStart.Desktop/Assets/AppLogos/localsend.svg",
            ["microsoft-teams"] = "avares://AgenStart.Desktop/Assets/AppLogos/microsoft-teams.svg",
            ["nodejs"] = "avares://AgenStart.Desktop/Assets/AppLogos/nodejs.svg",
            ["notepad-plus-plus"] = "avares://AgenStart.Desktop/Assets/AppLogos/notepad-plus-plus.svg",
            ["obs-studio"] = "avares://AgenStart.Desktop/Assets/AppLogos/obs-studio.svg",
            ["obsidian"] = "avares://AgenStart.Desktop/Assets/AppLogos/obsidian.svg",
            ["playnite"] = "avares://AgenStart.Desktop/Assets/AppLogos/playnite.svg",
            ["plex"] = "avares://AgenStart.Desktop/Assets/AppLogos/plex.svg",
            ["plexamp"] = "avares://AgenStart.Desktop/Assets/AppLogos/plexamp.svg",
            ["postman"] = "avares://AgenStart.Desktop/Assets/AppLogos/postman.svg",
            ["postgresql"] = "avares://AgenStart.Desktop/Assets/AppLogos/postgresql.svg",
            ["powershell"] = "avares://AgenStart.Desktop/Assets/AppLogos/powershell.svg",
            ["powertoys"] = "avares://AgenStart.Desktop/Assets/AppLogos/microsoft-powertoys.svg",
            ["python"] = "avares://AgenStart.Desktop/Assets/AppLogos/python.svg",
            ["quicklook"] = "avares://AgenStart.Desktop/Assets/AppLogos/quicklook.svg",
            ["sharex"] = "avares://AgenStart.Desktop/Assets/AppLogos/sharex.svg",
            ["signal"] = "avares://AgenStart.Desktop/Assets/AppLogos/signal.svg",
            ["slack"] = "avares://AgenStart.Desktop/Assets/AppLogos/slack.svg",
            ["spotify"] = "avares://AgenStart.Desktop/Assets/AppLogos/spotify.svg",
            ["steam"] = "avares://AgenStart.Desktop/Assets/AppLogos/steam.svg",
            ["stremio"] = "avares://AgenStart.Desktop/Assets/AppLogos/stremio.svg",
            ["sumatrapdf"] = "avares://AgenStart.Desktop/Assets/AppLogos/sumatrapdf.svg",
            ["telegram"] = "avares://AgenStart.Desktop/Assets/AppLogos/telegram.svg",
            ["termius"] = "avares://AgenStart.Desktop/Assets/ApplicationLogos/termius.png",
            ["thunderbird"] = "avares://AgenStart.Desktop/Assets/AppLogos/thunderbird.svg",
            ["ubisoft-connect"] = "avares://AgenStart.Desktop/Assets/AppLogos/ubisoft-connect.svg",
            ["visual-studio-code"] = "avares://AgenStart.Desktop/Assets/AppLogos/visual-studio-code.svg",
            ["vlc"] = "avares://AgenStart.Desktop/Assets/AppLogos/vlc-media-player.svg",
            ["windows-terminal"] = "avares://AgenStart.Desktop/Assets/AppLogos/windows-terminal.svg",
            ["wiztree"] = "avares://AgenStart.Desktop/Assets/AppLogos/wiztree.svg",
            ["xournalpp"] = "avares://AgenStart.Desktop/Assets/AppLogos/xournalpp.svg",
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
            if (assetPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var source = SvgSource.Load(assetPath, AssetBaseUri);
                return source is null ? null : new SvgImage { Source = source };
            }

            using var assetStream = AssetLoader.Open(new Uri(assetPath));
            return new Bitmap(assetStream);
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
