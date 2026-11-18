namespace AgenStart.Uninstall;

/// <summary>
/// Windows path arithmetic done in plain code, so the safety rules behave the same on every OS the
/// tests run on. Paths are compared ordinally, ignoring case. Relative paths and <c>..</c> segments
/// are refused rather than resolved: a deletion boundary never guesses.
/// </summary>
public static class WindowsPath
{
    public const string LongPathPrefix = @"\\?\";

    private const string LongUncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Returns the canonical form (<c>C:\A\B</c>, no trailing separator except on a drive root,
    /// no <c>\\?\</c> prefix), or null when the path is not absolute or is not safe to reason about.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = path.Trim().Trim('"').Replace('/', '\\');
        if (value.StartsWith(LongUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = @"\\" + value[LongUncPrefix.Length..];
        }
        else if (value.StartsWith(LongPathPrefix, StringComparison.Ordinal))
        {
            value = value[LongPathPrefix.Length..];
        }

        string root;
        string rest;
        if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
        {
            if (value.Length > 2 && value[2] != '\\')
            {
                return null;
            }

            root = char.ToUpperInvariant(value[0]) + @":\";
            rest = value.Length > 3 ? value[3..] : string.Empty;
        }
        else if (value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = value[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return null;
            }

            root = $@"\\{parts[0]}\{parts[1]}\";
            rest = string.Join('\\', parts.Skip(2));
        }
        else
        {
            return null;
        }

        var segments = rest.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.IndexOfAny(['*', '?', '<', '>', '|', '"', ':']) >= 0)
            {
                return null;
            }
        }

        var trimmed = segments.Select(segment => segment.TrimEnd(' ', '.')).ToArray();
        if (trimmed.Any(segment => segment.Length == 0))
        {
            return null;
        }

        return trimmed.Length == 0 ? root : root + string.Join('\\', trimmed);
    }

    /// <summary>The drive or share root of a normalized path (<c>C:\</c>, <c>\\server\share\</c>).</summary>
    public static string Root(string normalized)
    {
        if (normalized.Length >= 3 && normalized[1] == ':')
        {
            return normalized[..3];
        }

        var parts = normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return $@"\\{parts[0]}\{parts[1]}\";
    }

    /// <summary>Segments below the root.</summary>
    public static IReadOnlyList<string> Segments(string normalized)
    {
        var root = Root(normalized);
        return normalized.Length <= root.Length
            ? []
            : normalized[root.Length..].Split('\\');
    }

    public static bool IsRoot(string normalized) => Segments(normalized).Count == 0;

    public static bool AreEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is strictly below <paramref name="ancestor"/>.</summary>
    public static bool IsStrictlyUnder(string path, string ancestor)
    {
        if (path.Length <= ancestor.Length || !path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ancestor.EndsWith('\\') || path[ancestor.Length] == '\\';
    }

    public static bool IsSameOrUnder(string path, string ancestor) =>
        AreEqual(path, ancestor) || IsStrictlyUnder(path, ancestor);

    /// <summary>Number of segments between <paramref name="ancestor"/> and <paramref name="path"/>.</summary>
    public static int DepthBelow(string path, string ancestor) =>
        Segments(path).Count - Segments(ancestor).Count;

    public static string Combine(string normalized, string child) =>
        normalized.EndsWith('\\') ? normalized + child : normalized + "\\" + child;

    public static string? Parent(string normalized)
    {
        if (IsRoot(normalized))
        {
            return null;
        }

        var root = Root(normalized);
        var index = normalized.LastIndexOf('\\');
        return index < root.Length ? root : normalized[..index];
    }

    public static string FileName(string normalized)
    {
        var index = normalized.LastIndexOf('\\');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    public static string Extension(string normalized)
    {
        var name = FileName(normalized);
        var index = name.LastIndexOf('.');
        return index <= 0 ? string.Empty : name[index..];
    }
}
