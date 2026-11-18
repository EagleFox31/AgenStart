namespace AgenStart.Uninstall;

/// <summary>
/// Registry key paths in a canonical <c>HKCU\Software\…</c> / <c>HKLM\SOFTWARE\…</c> form. Only the
/// two hives the uninstall module may ever read are accepted.
/// </summary>
public static class RegistryPath
{
    public const string CurrentUser = "HKCU";
    public const string LocalMachine = "HKLM";

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Trim().Replace('/', '\\').Split('\\');
        if (segments.Any(segment => segment.Trim().Length == 0 || segment.IndexOfAny(['*', '?', '{', '}']) >= 0))
        {
            return null;
        }

        var hive = segments[0].Trim().ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => LocalMachine,
            _ => null
        };
        return hive is null
            ? null
            : string.Join('\\', segments.Skip(1).Select(segment => segment.Trim()).Prepend(hive));
    }

    public static string Hive(string normalized) => normalized.Split('\\')[0];

    /// <summary>Segments below the hive.</summary>
    public static IReadOnlyList<string> Segments(string normalized) => normalized.Split('\\')[1..];

    public static bool IsSameOrUnder(string path, string ancestor) =>
        string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(ancestor + "\\", StringComparison.OrdinalIgnoreCase);
}
