using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

/// <summary>
/// Reads the leftover rule file (ADR-0006) and fails closed: an unknown schema version, field,
/// placeholder, kind or data class makes the whole file invalid. The caller then shows the official
/// uninstall only, never a guessed list of leftovers.
/// </summary>
public static partial class UninstallRuleSetLoader
{
    public const string SupportedSchemaVersion = "1.0";

    public const int MinimumConfidence = -20;

    public const int MaximumConfidence = 20;

    public const int DefaultProductConfidence = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    public static UninstallRuleSet Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        RuleSetDto? document;
        try
        {
            document = JsonSerializer.Deserialize<RuleSetDto>(stream, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The uninstall rules are malformed or contain unsupported fields.", exception);
        }

        return Map(document ?? throw new InvalidDataException("The uninstall rules are empty."));
    }

    public static UninstallRuleSet Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return Load(stream);
    }

    private static UninstallRuleSet Map(RuleSetDto document)
    {
        if (document.SchemaVersion != SupportedSchemaVersion)
        {
            throw Invalid($"schemaVersion '{document.SchemaVersion}' is not supported (expected {SupportedSchemaVersion})");
        }

        if (string.IsNullOrWhiteSpace(document.RulesVersion))
        {
            throw Invalid("rulesVersion is required");
        }

        var forbidden = document.ExtraForbiddenPaths ?? [];
        foreach (var path in forbidden)
        {
            Check(RuleTemplate.ValidateExplicitPath(path), "extraForbiddenPaths");
        }

        var stoplist = document.GenericNameStoplist ?? [];
        if (stoplist.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid("genericNameStoplist holds an empty word");
        }

        var thresholds = document.VolumeThresholds is null
            ? VolumeThresholds.Default
            : new VolumeThresholds(
                Positive(document.VolumeThresholds.ConfirmAboveBytes, long.MaxValue, "volumeThresholds.confirmAboveBytes"),
                (int)Positive(document.VolumeThresholds.ConfirmAboveFileCount, int.MaxValue, "volumeThresholds.confirmAboveFileCount"));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var generic = (document.GenericRules ?? []).Select(rule => MapGeneric(rule, ids)).ToArray();
        var products = (document.ProductRules ?? []).Select(rule => MapProduct(rule, ids)).ToArray();

        return new UninstallRuleSet(
            document.SchemaVersion,
            document.RulesVersion,
            forbidden.ToArray(),
            stoplist.Select(word => word.Trim()).ToArray(),
            thresholds,
            generic,
            products);
    }

    private static GenericLeftoverRule MapGeneric(GenericRuleDto rule, HashSet<string> ids)
    {
        var id = RuleId(rule.Id, ids);
        var kind = Kind(rule.Kind, id);
        if (kind is not (LeftoverKind.Directory or LeftoverKind.File or LeftoverKind.Shortcut or LeftoverKind.RegistryKey))
        {
            throw Invalid($"rule '{id}': kind '{rule.Kind}' is not supported by generic rules");
        }

        var roots = rule.Roots ?? [];
        if (roots.Count == 0)
        {
            throw Invalid($"rule '{id}': roots are required");
        }

        foreach (var root in roots)
        {
            Check(RuleTemplate.ValidateRoot(root, kind), $"rule '{id}'");
        }

        Check(RuleTemplate.ValidateRelativePath(rule.RelativePath), $"rule '{id}'");
        var anchoredOnInstall = roots.All(root => root == RuleTemplate.InstallLocation);
        if (rule.RelativePath is null && kind != LeftoverKind.Shortcut && !anchoredOnInstall)
        {
            throw Invalid($"rule '{id}': relativePath is required unless the rule scans shortcuts or targets {RuleTemplate.InstallLocation}");
        }

        if (kind == LeftoverKind.Shortcut && rule.RelativePath is not null)
        {
            throw Invalid($"rule '{id}': a shortcut rule scans its roots and takes no relativePath");
        }

        return new GenericLeftoverRule(id, kind, roots.ToArray(), rule.RelativePath, DataClass(rule.DataClass, id), Confidence(rule.Confidence, id));
    }

    private static ProductLeftoverRule MapProduct(ProductRuleDto rule, HashSet<string> ids)
    {
        var id = RuleId(rule.Id, ids);
        var publishers = rule.Match?.Publisher ?? [];
        var names = rule.Match?.DisplayName ?? [];
        if (names.Count == 0 || names.Any(string.IsNullOrWhiteSpace) || publishers.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid($"rule '{id}': match needs at least one displayName and no empty value");
        }

        var leftovers = (rule.Leftovers ?? []).Select(leftover =>
        {
            var kind = Kind(leftover.Kind, id);
            if (kind is not (LeftoverKind.Directory or LeftoverKind.File or LeftoverKind.RegistryKey))
            {
                throw Invalid($"rule '{id}': kind '{leftover.Kind}' is not supported by product rules");
            }

            Check(RuleTemplate.ValidateExplicitPath(leftover.Path), $"rule '{id}'");
            if (RuleTemplate.IsRegistryPath(leftover.Path!) != (kind == LeftoverKind.RegistryKey))
            {
                throw Invalid($"rule '{id}': path '{leftover.Path}' does not fit kind '{leftover.Kind}'");
            }

            return new ProductLeftover(
                kind,
                leftover.Path!,
                DataClass(leftover.DataClass, id),
                leftover.Confidence is null ? DefaultProductConfidence : Confidence(leftover.Confidence, id));
        }).ToArray();

        if (leftovers.Length == 0)
        {
            throw Invalid($"rule '{id}': leftovers are required");
        }

        var parents = rule.RemoveParentIfEmpty ?? [];
        foreach (var parent in parents)
        {
            Check(RuleTemplate.ValidateExplicitPath(parent), $"rule '{id}'");
        }

        return new ProductLeftoverRule(id, new ProductRuleMatch(publishers.ToArray(), names.ToArray()), leftovers, parents.ToArray());
    }

    private static string RuleId(string? id, HashSet<string> ids)
    {
        if (id is null || !RuleIdPattern().IsMatch(id))
        {
            throw Invalid($"rule id '{id}' must be lowercase words separated by '.' or '-'");
        }

        return ids.Add(id) ? id : throw Invalid($"rule id '{id}' is duplicated");
    }

    private static LeftoverKind Kind(string? token, string id) =>
        UninstallTokens.TryParseKind(token, out var kind) ? kind : throw Invalid($"rule '{id}': unknown kind '{token}'");

    private static DataClass DataClass(string? token, string id) =>
        UninstallTokens.TryParseDataClass(token, out var dataClass) ? dataClass : throw Invalid($"rule '{id}': unknown dataClass '{token}'");

    private static int Confidence(int? value, string id) =>
        value is >= MinimumConfidence and <= MaximumConfidence
            ? value.Value
            : throw Invalid($"rule '{id}': confidence must be between {MinimumConfidence} and {MaximumConfidence}");

    private static long Positive(long? value, long maximum, string name) =>
        value > 0 && value <= maximum ? value.Value : throw Invalid($"{name} must be a positive number");

    private static void Check(string? error, string context)
    {
        if (error is not null)
        {
            throw Invalid($"{context}: {error}");
        }
    }

    private static InvalidDataException Invalid(string message) => new($"The uninstall rules are invalid: {message}.");

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$")]
    private static partial Regex RuleIdPattern();

    private sealed class RuleSetDto
    {
        public string? SchemaVersion { get; init; }
        public string? RulesVersion { get; init; }
        public List<string>? ExtraForbiddenPaths { get; init; }
        public List<string>? GenericNameStoplist { get; init; }
        public VolumeThresholdsDto? VolumeThresholds { get; init; }
        public List<GenericRuleDto>? GenericRules { get; init; }
        public List<ProductRuleDto>? ProductRules { get; init; }
    }

    private sealed class VolumeThresholdsDto
    {
        public long? ConfirmAboveBytes { get; init; }
        public long? ConfirmAboveFileCount { get; init; }
    }

    private sealed class GenericRuleDto
    {
        public string? Id { get; init; }
        public string? Kind { get; init; }
        public List<string>? Roots { get; init; }
        public string? RelativePath { get; init; }
        public string? DataClass { get; init; }
        public int? Confidence { get; init; }
    }

    private sealed class ProductRuleDto
    {
        public string? Id { get; init; }
        public MatchDto? Match { get; init; }
        public List<LeftoverDto>? Leftovers { get; init; }
        public List<string>? RemoveParentIfEmpty { get; init; }
    }

    private sealed class MatchDto
    {
        public List<string>? Publisher { get; init; }
        public List<string>? DisplayName { get; init; }
    }

    private sealed class LeftoverDto
    {
        public string? Kind { get; init; }
        public string? Path { get; init; }
        public string? DataClass { get; init; }
        public int? Confidence { get; init; }
    }
}
