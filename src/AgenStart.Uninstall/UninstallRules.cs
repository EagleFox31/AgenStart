namespace AgenStart.Uninstall;

public enum LeftoverKind
{
    Directory = 1,
    File = 2,
    Shortcut = 3,
    RegistryKey = 4,
    RegistryValue = 5,
    ScheduledTask = 6,
    FirewallRule = 7
}

public enum DataClass
{
    AppData = 1,
    Cache = 2,
    Configuration = 3,
    UserData = 4,
    Shortcut = 5
}

/// <summary>
/// Persisted and displayed tokens for the uninstall enums. They are an explicit mapping, never an
/// enum name (LL-003), and the rule file and the exported plan both use them.
/// </summary>
public static class UninstallTokens
{
    private static readonly (LeftoverKind Kind, string Token)[] Kinds =
    [
        (LeftoverKind.Directory, "directory"),
        (LeftoverKind.File, "file"),
        (LeftoverKind.Shortcut, "shortcut"),
        (LeftoverKind.RegistryKey, "registryKey"),
        (LeftoverKind.RegistryValue, "registryValue"),
        (LeftoverKind.ScheduledTask, "scheduledTask"),
        (LeftoverKind.FirewallRule, "firewallRule")
    ];

    private static readonly (DataClass DataClass, string Token)[] DataClasses =
    [
        (DataClass.AppData, "appData"),
        (DataClass.Cache, "cache"),
        (DataClass.Configuration, "configuration"),
        (DataClass.UserData, "userData"),
        (DataClass.Shortcut, "shortcut")
    ];

    public static string Kind(LeftoverKind kind) =>
        Kinds.Single(entry => entry.Kind == kind).Token;

    public static bool TryParseKind(string? token, out LeftoverKind kind)
    {
        foreach (var entry in Kinds)
        {
            if (string.Equals(entry.Token, token, StringComparison.Ordinal))
            {
                kind = entry.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public static string DataClassToken(DataClass dataClass) =>
        DataClasses.Single(entry => entry.DataClass == dataClass).Token;

    public static bool TryParseDataClass(string? token, out DataClass dataClass)
    {
        foreach (var entry in DataClasses)
        {
            if (string.Equals(entry.Token, token, StringComparison.Ordinal))
            {
                dataClass = entry.DataClass;
                return true;
            }
        }

        dataClass = default;
        return false;
    }
}

/// <summary>
/// A generic pattern: for every root, the target is <see cref="RelativePath"/> built from the
/// program's publisher and product names. A shortcut rule scans its roots for <c>.lnk</c> files instead.
/// </summary>
public sealed record GenericLeftoverRule(
    string Id,
    LeftoverKind Kind,
    IReadOnlyList<string> Roots,
    string? RelativePath,
    DataClass DataClass,
    int Confidence);

public sealed record ProductRuleMatch(IReadOnlyList<string> Publishers, IReadOnlyList<string> DisplayNames);

public sealed record ProductLeftover(LeftoverKind Kind, string Path, DataClass DataClass, int Confidence);

/// <summary>Explicit leftovers for one product, matched by exact (normalized) publisher and display name.</summary>
public sealed record ProductLeftoverRule(
    string Id,
    ProductRuleMatch Match,
    IReadOnlyList<ProductLeftover> Leftovers,
    IReadOnlyList<string> RemoveParentIfEmpty);

/// <summary>Above these totals the confirmation asks for one more approval (ADR-0005 §8).</summary>
public sealed record VolumeThresholds(long ConfirmAboveBytes, int ConfirmAboveFileCount)
{
    public static VolumeThresholds Default { get; } = new(2L * 1024 * 1024 * 1024, 5_000);
}

/// <summary>The leftover rules shipped with the app (ADR-0006). The safety floor is not here: it is code.</summary>
public sealed record UninstallRuleSet(
    string SchemaVersion,
    string RulesVersion,
    IReadOnlyList<string> ExtraForbiddenPaths,
    IReadOnlyList<string> GenericNameStoplist,
    VolumeThresholds VolumeThresholds,
    IReadOnlyList<GenericLeftoverRule> GenericRules,
    IReadOnlyList<ProductLeftoverRule> ProductRules);
