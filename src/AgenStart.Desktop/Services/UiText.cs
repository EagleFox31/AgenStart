namespace AgenStart.Desktop.Services;

/// <summary>
/// A UI string kept as a dictionary key plus arguments and resolved in the current language when
/// read, so a language switch re-renders it without recomputing the state that produced it.
/// Arguments may themselves be <see cref="UiText"/> (for example a size unit inside a sentence).
/// </summary>
public sealed class UiText
{
    private readonly string? _key;
    private readonly string _raw;
    private readonly int? _count;
    private readonly object?[] _args;
    private readonly Func<ILocalizer, string>? _resolve;

    private UiText(string? key, string raw, int? count, object?[] args, Func<ILocalizer, string>? resolve = null)
    {
        _key = key;
        _raw = raw;
        _count = count;
        _args = args;
        _resolve = resolve;
    }

    public static UiText Empty { get; } = Raw(string.Empty);

    public static UiText Key(string key, params object?[] args) => new(key, string.Empty, null, args);

    /// <summary>Picks <c>{key}.One</c> or <c>{key}.Other</c>; the count is <c>{0}</c>, extra arguments follow.</summary>
    public static UiText Plural(string key, int count, params object?[] args) => new(key, string.Empty, count, args);

    /// <summary>Text that is not translated: names, versions, engine or provider messages.</summary>
    public static UiText Raw(string? text) => new(null, text ?? string.Empty, null, []);

    /// <summary>Text translated outside the UI dictionaries, such as profile labels from the profile registry.</summary>
    public static UiText Dynamic(Func<ILocalizer, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return new(null, string.Empty, null, [], resolve);
    }

    public string Resolve(ILocalizer text)
    {
        if (_resolve is not null)
        {
            return _resolve(text);
        }

        if (_key is null)
        {
            return _raw;
        }

        var args = Array.ConvertAll(_args, arg => arg is UiText nested ? nested.Resolve(text) : arg);
        if (_count is { } count)
        {
            return text.Plural(_key, count, args);
        }

        return args.Length == 0 ? text[_key] : text.Format(_key, args);
    }
}

/// <summary>Byte sizes with the unit translated (GB/Go) and the number in the UI culture.</summary>
public static class UiUnits
{
    public static UiText Bytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => UiText.Key("Units.Gigabytes", bytes / (1024d * 1024 * 1024)),
        >= 1024L * 1024 => UiText.Key("Units.Megabytes", bytes / (1024d * 1024)),
        >= 1024 => UiText.Key("Units.Kilobytes", bytes / 1024d),
        _ => UiText.Key("Units.Bytes", bytes)
    };
}

/// <summary>
/// Base for list rows: they read the shared localizer when bound and are refreshed by their
/// owning step view model on a language change, so rows never subscribe to a static event.
/// </summary>
public abstract class LocalizedRow : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected static ILocalizer Text => AvaloniaLocalizationService.Shared;

    public void RefreshText() => OnPropertyChanged(string.Empty);

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
