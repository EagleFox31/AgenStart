using System.Globalization;

namespace AgenStart.Desktop.Services;

/// <summary>Supported UI languages; each one is a string dictionary under Strings/.</summary>
public static class AppLanguages
{
    public const string English = "en";
    public const string French = "fr";
    public const string Default = English;

    public static IReadOnlyList<string> All { get; } = [English, French];

    /// <summary>Maps any stored value ("fr", "FR", "fr-CA") to a supported language, else <see cref="Default"/>.</summary>
    public static string Normalize(string? language)
    {
        var value = language?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return Default;
        }

        var dash = value.IndexOfAny(['-', '_']);
        var neutral = dash > 0 ? value[..dash] : value;
        return All.FirstOrDefault(name => string.Equals(name, neutral, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }

    /// <summary>First-run default: the OS display language when supported, English otherwise.</summary>
    public static string FromCulture(CultureInfo? culture) => Normalize(culture?.Name);

    /// <summary>The name a speaker of that language would recognise, never translated.</summary>
    public static string NativeName(string language) => Normalize(language) switch
    {
        French => "Français",
        _ => "English"
    };

    /// <summary>CLDR plural category: French treats 0 and 1 as singular, English only 1.</summary>
    public static bool IsSingular(string language, int count) =>
        Normalize(language) == French ? count is 0 or 1 : count == 1;
}

/// <summary>Read side of the UI strings, injected into view models.</summary>
public interface ILocalizer
{
    string Language { get; }

    CultureInfo Culture { get; }

    /// <summary>The string for <paramref name="key"/>; English, then the key itself, when missing.</summary>
    string this[string key] { get; }

    event EventHandler? LanguageChanged;

    string Format(string key, params object?[] args);

    /// <summary>Formats <c>{key}.One</c> or <c>{key}.Other</c> with the count as <c>{0}</c>.</summary>
    string Plural(string key, int count, params object?[] extraArgs);
}

public interface ILocalizationService : ILocalizer
{
    void Apply(string? language);
}

/// <summary>Shared formatting rules, so every localizer pluralises and formats identically.</summary>
public abstract class LocalizerBase : ILocalizer
{
    public abstract string Language { get; }

    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    public abstract string this[string key] { get; }

    public event EventHandler? LanguageChanged;

    public string Format(string key, params object?[] args)
    {
        var template = this[key];
        try
        {
            return string.Format(Culture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string Plural(string key, int count, params object?[] extraArgs)
    {
        var form = AppLanguages.IsSingular(Language, count) ? ".One" : ".Other";
        return Format(key + form, [count, .. extraArgs]);
    }

    protected void RaiseLanguageChanged() => LanguageChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// English (Strings/en.axaml) is merged permanently in App.axaml; another language is an overlay
/// merged on top, so any key it lacks falls back to English for both XAML and C# lookups.
/// </summary>
public sealed class AvaloniaLocalizationService : LocalizerBase, ILocalizationService
{
    private readonly ResourceDictionarySwapper _swapper = new();
    private string _language = AppLanguages.Default;

    public static AvaloniaLocalizationService Shared { get; } = new();

    public override string Language => _language;

    public override string this[string key] =>
        Avalonia.Application.Current is { } app
        && app.TryGetResource(key, null, out var value)
        && value is string text
            ? text
            : key;

    public void Apply(string? language)
    {
        // English is always merged, so the initial state (no overlay) is already "en".
        var name = AppLanguages.Normalize(language);
        if (name == _language || Avalonia.Application.Current is not { } app)
        {
            return;
        }

        _swapper.Swap(app, name == AppLanguages.Default ? null : $"Strings/{name}.axaml");
        _language = name;
        CultureInfo.CurrentUICulture = Culture;
        RaiseLanguageChanged();
    }
}
