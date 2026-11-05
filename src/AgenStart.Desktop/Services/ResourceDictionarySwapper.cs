using Avalonia.Markup.Xaml.Styling;

namespace AgenStart.Desktop.Services;

/// <summary>
/// Keeps one swappable ResourceInclude in Application.Resources (a palette, a language overlay).
/// The new dictionary is added before the old one is removed, so no DynamicResource lookup ever
/// falls through to a missing key during the switch.
/// </summary>
internal sealed class ResourceDictionarySwapper
{
    private static readonly Uri BaseUri = new("avares://AgenStart.Desktop/");
    private ResourceInclude? _active;

    /// <summary>Activates <paramref name="relativePath"/>, or removes the overlay when it is null.</summary>
    public ResourceInclude? Swap(Avalonia.Application app, string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(app);
        var merged = app.Resources.MergedDictionaries;

        ResourceInclude? include = null;
        if (relativePath is not null)
        {
            include = new ResourceInclude(BaseUri) { Source = new Uri(BaseUri, relativePath) };
            merged.Add(include);
        }

        if (_active is not null)
        {
            merged.Remove(_active);
        }

        _active = include;
        return include;
    }
}
