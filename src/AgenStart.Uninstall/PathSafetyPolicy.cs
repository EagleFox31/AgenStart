namespace AgenStart.Uninstall;

public enum SafetyVerdict
{
    Allowed = 1,
    Invalid = 2,
    Forbidden = 3,
    AncestorOfForbidden = 4,
    TooShallow = 5
}

/// <summary>
/// The hard-coded safety floor of ADR-0005 and the design §5. Every candidate goes through it before
/// it can enter a plan. The rule file can add forbidden paths and stop words; it can never remove
/// one of the built-in ones, because they are not read from it.
/// </summary>
public sealed class PathSafetyPolicy
{
    /// <summary>Names too generic to identify a program, whatever the rule file says.</summary>
    public static readonly IReadOnlyList<string> BuiltInStoplist =
        ["common", "shared", "data", "games", "launcher", "update", "cache", "temp", "microsoft", "windows"];

    public const int MinimumNameLength = 4;

    private static readonly string[] ForbiddenRegistrySegments = ["Microsoft", "Classes", "Policies"];

    private readonly List<string> _forbiddenRoots = [];
    private readonly List<string> _forbiddenSubtrees = [];
    private readonly List<string> _forbiddenRegistry = [];
    private readonly List<string> _knownRoots = [];
    private readonly List<string> _userDataRoots = [];
    private readonly HashSet<string> _stoplist;
    private readonly string? _userProfiles;
    private readonly string? _currentProfile;
    private readonly string? _publicProfile;

    private PathSafetyPolicy(IEnumerable<string> stoplist)
    {
        _stoplist = new HashSet<string>(stoplist.Select(ProgramNameNormalizer.Normalize), StringComparer.Ordinal);
        _stoplist.UnionWith(BuiltInStoplist);
    }

    private PathSafetyPolicy(
        IKnownFolderResolver folders,
        IEnumerable<string> stoplist,
        string? agenStartDataDirectory)
        : this(stoplist)
    {
        foreach (var folder in Enum.GetValues<KnownFolder>())
        {
            if (WindowsPath.Normalize(folders.Resolve(folder)) is { } path)
            {
                _forbiddenRoots.Add(path);
                _knownRoots.Add(path);
            }
        }

        AddSubtree(folders.Resolve(KnownFolder.Windows));
        AddSubtree(Child(folders.Resolve(KnownFolder.ProgramFiles), "WindowsApps"));
        AddSubtree(Child(folders.Resolve(KnownFolder.LocalAppData), "Packages"));
        AddSubtree(Child(folders.Resolve(KnownFolder.LocalAppData), "AgenStart"));
        AddSubtree(agenStartDataDirectory);

        foreach (var folder in new[] { KnownFolder.OneDrive, KnownFolder.OneDriveConsumer, KnownFolder.OneDriveCommercial })
        {
            if (WindowsPath.Normalize(folders.Resolve(folder)) is { } path)
            {
                _userDataRoots.Add(path);
            }
        }

        _userProfiles = WindowsPath.Normalize(folders.Resolve(KnownFolder.UserProfiles));
        _currentProfile = WindowsPath.Normalize(folders.Resolve(KnownFolder.UserProfile));
        var publicDesktop = WindowsPath.Normalize(folders.Resolve(KnownFolder.PublicDesktop));
        _publicProfile = publicDesktop is null ? null : WindowsPath.Parent(publicDesktop);
    }

    /// <summary>Builds the policy for this machine. <paramref name="rules"/> may be null: the floor still applies.</summary>
    public static PathSafetyPolicy Create(IKnownFolderResolver folders, UninstallRuleSet? rules, string? agenStartDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var policy = new PathSafetyPolicy(folders, rules?.GenericNameStoplist ?? [], agenStartDataDirectory);
        foreach (var template in rules?.ExtraForbiddenPaths ?? [])
        {
            var resolved = RuleTemplate.ResolveExplicit(template, folders, installLocation: null);
            if (resolved is null)
            {
                continue;
            }

            if (RuleTemplate.IsRegistryPath(template))
            {
                policy._forbiddenRegistry.Add(resolved);
            }
            else
            {
                policy._forbiddenRoots.Add(resolved);
            }
        }

        return policy;
    }

    /// <summary>Forbidden folders that the policy protects, for the property tests and diagnostics.</summary>
    public IReadOnlyList<string> ForbiddenRoots => _forbiddenRoots;

    public SafetyVerdict EvaluatePath(string? path)
    {
        var target = WindowsPath.Normalize(path);
        if (target is null)
        {
            return SafetyVerdict.Invalid;
        }

        if (WindowsPath.IsRoot(target)
            || _forbiddenRoots.Any(root => WindowsPath.AreEqual(target, root))
            || _forbiddenSubtrees.Any(subtree => WindowsPath.IsSameOrUnder(target, subtree)))
        {
            return SafetyVerdict.Forbidden;
        }

        if (_forbiddenRoots.Concat(_forbiddenSubtrees).Any(root => WindowsPath.IsStrictlyUnder(root, target)))
        {
            return SafetyVerdict.AncestorOfForbidden;
        }

        if (_userProfiles is not null
            && WindowsPath.IsStrictlyUnder(target, _userProfiles)
            && !(_currentProfile is not null && WindowsPath.IsStrictlyUnder(target, _currentProfile))
            && !(_publicProfile is not null && WindowsPath.IsStrictlyUnder(target, _publicProfile)))
        {
            // Another account's profile, or a profile folder itself: never this program's leftover.
            return SafetyVerdict.Forbidden;
        }

        var underKnownRoot = _knownRoots.Any(root => WindowsPath.IsStrictlyUnder(target, root));
        if (!underKnownRoot && WindowsPath.Segments(target).Count < 2)
        {
            return SafetyVerdict.TooShallow;
        }

        return SafetyVerdict.Allowed;
    }

    public SafetyVerdict EvaluateRegistryKey(string? keyPath)
    {
        var key = RegistryPath.Normalize(keyPath);
        if (key is null)
        {
            return SafetyVerdict.Invalid;
        }

        var segments = RegistryPath.Segments(key).ToList();
        if (segments.Count == 0 || !string.Equals(segments[0], "SOFTWARE", StringComparison.OrdinalIgnoreCase))
        {
            return SafetyVerdict.Forbidden;
        }

        segments.RemoveAt(0);
        if (segments.Count > 0 && string.Equals(segments[0], "Wow6432Node", StringComparison.OrdinalIgnoreCase))
        {
            segments.RemoveAt(0);
        }

        if (segments.Count > 0
            && ForbiddenRegistrySegments.Any(name => string.Equals(segments[0], name, StringComparison.OrdinalIgnoreCase)))
        {
            return SafetyVerdict.Forbidden;
        }

        if (_forbiddenRegistry.Any(forbidden => RegistryPath.IsSameOrUnder(key, forbidden)))
        {
            return SafetyVerdict.Forbidden;
        }

        if (_forbiddenRegistry.Any(forbidden => RegistryPath.IsSameOrUnder(forbidden, key)))
        {
            return SafetyVerdict.AncestorOfForbidden;
        }

        var minimumDepth = RegistryPath.Hive(key) == RegistryPath.LocalMachine ? 2 : 1;
        return segments.Count < minimumDepth ? SafetyVerdict.TooShallow : SafetyVerdict.Allowed;
    }

    /// <summary>True for files the user owns whatever the rule says: everything under OneDrive.</summary>
    public bool IsUserData(string path)
    {
        var target = WindowsPath.Normalize(path);
        return target is not null && _userDataRoots.Any(root => WindowsPath.IsSameOrUnder(target, root));
    }

    /// <summary>True when the parent of <paramref name="path"/> is one of the machine's known folders.</summary>
    public bool IsDirectlyUnderKnownFolder(string path)
    {
        var parent = WindowsPath.Normalize(path) is { } target ? WindowsPath.Parent(target) : null;
        return parent is not null && _knownRoots.Any(root => WindowsPath.AreEqual(root, parent));
    }

    /// <summary>A name may identify a program only when it is long enough and not a generic word.</summary>
    public bool IsDistinctiveName(string? normalizedName) =>
        normalizedName is { Length: >= MinimumNameLength } && !_stoplist.Contains(normalizedName);

    private void AddSubtree(string? path)
    {
        if (WindowsPath.Normalize(path) is { } normalized)
        {
            _forbiddenSubtrees.Add(normalized);
        }
    }

    private static string? Child(string? folder, string name) =>
        WindowsPath.Normalize(folder) is { } normalized ? WindowsPath.Combine(normalized, name) : null;
}
