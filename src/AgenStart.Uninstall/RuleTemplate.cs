using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

/// <summary>
/// Placeholders allowed in rule paths. Folder placeholders open a path and resolve through
/// <see cref="IKnownFolderResolver"/>; name placeholders only appear in generic relative paths.
/// Anything else between braces is an unknown placeholder and the rule file is refused.
/// </summary>
public static partial class RuleTemplate
{
    public const string InstallLocation = "{InstallLocation}";
    public const string Publisher = "{Publisher}";
    public const string Product = "{Product}";

    private static readonly Dictionary<string, KnownFolder> Folders = new(StringComparer.Ordinal)
    {
        ["{LocalAppData}"] = KnownFolder.LocalAppData,
        ["{RoamingAppData}"] = KnownFolder.RoamingAppData,
        ["{LocalLow}"] = KnownFolder.LocalLow,
        ["{ProgramData}"] = KnownFolder.ProgramData,
        ["{Documents}"] = KnownFolder.Documents,
        ["{SavedGames}"] = KnownFolder.SavedGames,
        ["{Desktop}"] = KnownFolder.Desktop,
        ["{PublicDesktop}"] = KnownFolder.PublicDesktop,
        ["{StartMenuPrograms}"] = KnownFolder.StartMenuPrograms,
        ["{CommonStartMenuPrograms}"] = KnownFolder.CommonStartMenuPrograms,
        ["{ProgramFiles}"] = KnownFolder.ProgramFiles,
        ["{ProgramFilesX86}"] = KnownFolder.ProgramFilesX86
    };

    private static readonly string[] RegistryRoots = [@"HKCU\", @"HKLM\"];

    public static bool IsRegistryPath(string template) =>
        RegistryRoots.Any(root => template.StartsWith(root, StringComparison.OrdinalIgnoreCase));

    /// <summary>A root of a generic rule: one folder placeholder alone, or a registry path.</summary>
    public static string? ValidateRoot(string? template, LeftoverKind kind)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return "a root is empty";
        }

        if (kind is LeftoverKind.RegistryKey or LeftoverKind.RegistryValue)
        {
            return IsRegistryPath(template) && !HasPlaceholder(template)
                ? null
                : $"registry root '{template}' must start with HKCU\\ or HKLM\\ and have no placeholder";
        }

        return Folders.ContainsKey(template) || template == InstallLocation
            ? null
            : $"root '{template}' must be exactly one folder placeholder";
    }

    /// <summary>A generic relative path: literal segments and name placeholders only.</summary>
    public static string? ValidateRelativePath(string? template)
    {
        if (template is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(template))
        {
            return "relativePath is empty";
        }

        foreach (Match match in Placeholder().Matches(template))
        {
            if (match.Value is not (Publisher or Product))
            {
                return $"unknown placeholder '{match.Value}' in relativePath '{template}'";
            }
        }

        foreach (var segment in template.Trim('\\').Split('\\'))
        {
            if (segment != Publisher && segment != Product && HasPlaceholder(segment))
            {
                return $"placeholder in '{template}' must fill a whole segment";
            }
        }

        return ValidateSegments(template);
    }

    /// <summary>An explicit path: one leading folder placeholder then literal segments, or a registry path.</summary>
    public static string? ValidateExplicitPath(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return "a path is empty";
        }

        if (IsRegistryPath(template))
        {
            return HasPlaceholder(template) ? $"registry path '{template}' cannot hold a placeholder" : null;
        }

        var placeholders = Placeholder().Matches(template).Select(match => match.Value).ToArray();
        if (placeholders.Length != 1 || !template.StartsWith(placeholders[0], StringComparison.Ordinal))
        {
            return $"path '{template}' must start with exactly one folder placeholder";
        }

        if (!Folders.ContainsKey(placeholders[0]) && placeholders[0] != InstallLocation)
        {
            return $"unknown placeholder '{placeholders[0]}' in '{template}'";
        }

        var rest = template[placeholders[0].Length..];
        return rest.Length == 0 ? null : ValidateSegments(rest);
    }

    /// <summary>
    /// Resolves an explicit path. Returns null when its folder does not exist here, or when it would
    /// leave its folder; the safety policy still checks the result.
    /// </summary>
    public static string? ResolveExplicit(string template, IKnownFolderResolver folders, string? installLocation)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (IsRegistryPath(template))
        {
            return RegistryPath.Normalize(template);
        }

        var placeholder = Placeholder().Match(template);
        if (!placeholder.Success || placeholder.Index != 0)
        {
            return null;
        }

        var basePath = placeholder.Value == InstallLocation
            ? WindowsPath.Normalize(installLocation)
            : Folders.TryGetValue(placeholder.Value, out var folder) ? WindowsPath.Normalize(folders.Resolve(folder)) : null;
        if (basePath is null)
        {
            return null;
        }

        var rest = template[placeholder.Length..].Trim('\\');
        return rest.Length == 0 ? basePath : WindowsPath.Normalize(WindowsPath.Combine(basePath, rest));
    }

    private static bool HasPlaceholder(string template) => template.Contains('{', StringComparison.Ordinal) || template.Contains('}', StringComparison.Ordinal);

    private static string? ValidateSegments(string path)
    {
        var segments = path.Trim('\\').Split('\\');
        foreach (var segment in segments)
        {
            var literal = Placeholder().Replace(segment, "x");
            if (literal.Length == 0 || literal is "." or ".." || literal.IndexOfAny(['/', ':', '*', '?', '<', '>', '|', '"', '{', '}']) >= 0)
            {
                return $"segment '{segment}' in '{path}' is not a plain folder name";
            }
        }

        return null;
    }

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();
}
