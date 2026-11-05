using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace AgenStart.Desktop.Services;

/// <summary>Known palettes; each one is a ResourceDictionary under Styles/Palettes/.</summary>
public static class AppThemes
{
    public const string Default = "AgenStart";

    public static IReadOnlyList<string> All { get; } = ["AgenStart", "Apple", "Google", "Dark"];

    /// <summary>Maps any stored value to a known palette, falling back to <see cref="Default"/>.</summary>
    public static string Normalize(string? theme) =>
        All.FirstOrDefault(name => string.Equals(name, theme?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Default;
}

public interface IThemeService
{
    string Current { get; }

    void Apply(string? theme);
}

/// <summary>
/// Swaps the active palette in Application.Resources. Views reference palette keys with
/// DynamicResource, so the change is live. Applying the active palette again is a no-op.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    private static readonly Uri BaseUri = new("avares://AgenStart.Desktop/");
    private ResourceInclude? _active;

    public static AvaloniaThemeService Shared { get; } = new();

    public string Current { get; private set; } = string.Empty;

    public void Apply(string? theme)
    {
        var name = AppThemes.Normalize(theme);
        if (name == Current || Avalonia.Application.Current is not { } app)
        {
            return;
        }

        var include = new ResourceInclude(BaseUri)
        {
            Source = new Uri(BaseUri, $"Styles/Palettes/{name}.axaml")
        };

        // Add before removing so no lookup ever falls through to a missing key.
        var merged = app.Resources.MergedDictionaries;
        merged.Add(include);
        if (_active is not null)
        {
            merged.Remove(_active);
        }

        _active = include;
        Current = name;

        // Each palette declares its own variant, so Fluent controls (inputs, scrollbars,
        // menus) follow the palette without any per-theme branching here.
        var variant = ReadVariant(include);
        app.RequestedThemeVariant = variant;

        if (include.TryGetResource("AgenFluentAccentColor", variant, out var value) && value is Color accent)
        {
            foreach (var fluent in app.Styles.OfType<FluentTheme>())
            {
                if (fluent.Palettes.TryGetValue(variant, out var palette))
                {
                    palette.Accent = accent;
                }
            }
        }
    }

    private static ThemeVariant ReadVariant(ResourceInclude include) =>
        include.TryGetResource("AgenThemeVariant", ThemeVariant.Default, out var value)
        && value is string text
        && string.Equals(text.Trim(), "Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
}
