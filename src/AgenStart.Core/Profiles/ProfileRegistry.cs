using System.Text.Json;
using System.Text.Json.Serialization;
using AgenStart.Core.Catalogue;

namespace AgenStart.Core.Profiles;

public sealed class ProfileRegistry
{
    public const string EmbeddedResourceName = "AgenStart.Data.profiles.json";
    private const string SupportedSchemaVersion = "1.0.0";

    // Temporary bridge to the UserProfile flags enum until ADR-0003 rollout step 4
    // removes it; every active profile must have an entry here.
    private static readonly IReadOnlyDictionary<string, UserProfile> LegacyFlags =
        new Dictionary<string, UserProfile>(StringComparer.Ordinal)
        {
            ["personal"] = UserProfile.Personal,
            ["development"] = UserProfile.Development,
            ["business"] = UserProfile.Business,
            ["creative"] = UserProfile.Creative,
            ["learning"] = UserProfile.Learning,
            ["gaming"] = UserProfile.Gaming,
            ["entertainment"] = UserProfile.Entertainment,
            ["it-student"] = UserProfile.ItStudent
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly Lazy<ProfileRegistry> DefaultRegistry = new(LoadEmbedded);

    private readonly Dictionary<string, ProfileDefinition> _byIdOrAlias;

    private ProfileRegistry(IReadOnlyList<ProfileDefinition> profiles)
    {
        Profiles = profiles;
        Active = profiles.Where(static profile => profile.IsActive).ToArray();
        _byIdOrAlias = new Dictionary<string, ProfileDefinition>(StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            _byIdOrAlias.Add(profile.Id.Value, profile);
            foreach (var alias in profile.Aliases)
            {
                _byIdOrAlias.Add(alias.Value, profile);
            }
        }
    }

    public static ProfileRegistry Default => DefaultRegistry.Value;

    public IReadOnlyList<ProfileDefinition> Profiles { get; }

    public IReadOnlyList<ProfileDefinition> Active { get; }

    public static ProfileRegistry Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        RegistryDto? document;
        try
        {
            document = JsonSerializer.Deserialize<RegistryDto>(stream, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The profile registry is malformed or contains unsupported fields.", exception);
        }

        return Map(document ?? throw new InvalidDataException("The profile registry is empty."));
    }

    public static ProfileRegistry Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return Load(stream);
    }

    public bool TryResolve(string? idOrAlias, out ProfileDefinition profile)
    {
        var key = idOrAlias?.Trim().ToLowerInvariant();
        if (key is not null && _byIdOrAlias.TryGetValue(key, out var found))
        {
            profile = found;
            return true;
        }

        profile = null!;
        return false;
    }

    public ProfileDefinition Get(ProfileId id) =>
        _byIdOrAlias.TryGetValue(id.Value, out var profile) && profile.Id == id
            ? profile
            : throw new KeyNotFoundException($"Profile '{id}' is not in the registry.");

    public UserProfile ToUserProfile(ProfileId id) =>
        LegacyFlags.TryGetValue(id.Value, out var flag)
            ? flag
            : throw new InvalidOperationException($"Profile '{id}' has no legacy UserProfile mapping.");

    public ProfileDefinition Get(UserProfile singleProfile) =>
        Active.FirstOrDefault(profile => LegacyFlags[profile.Id.Value] == singleProfile)
        ?? throw new InvalidOperationException($"No active profile maps to {singleProfile}.");

    public IReadOnlyList<ProfileDefinition> Expand(UserProfile selection) =>
        Active.Where(profile => selection.HasFlag(LegacyFlags[profile.Id.Value])).ToArray();

    private static ProfileRegistry LoadEmbedded()
    {
        using var stream = typeof(ProfileRegistry).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded profile registry '{EmbeddedResourceName}' is missing.");
        return Load(stream);
    }

    private static ProfileRegistry Map(RegistryDto document)
    {
        if (!string.Equals(document.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported profile registry schema version '{document.SchemaVersion}'. Expected {SupportedSchemaVersion}.");
        }

        if (document.Profiles is null || document.Profiles.Count == 0)
        {
            throw new InvalidDataException("The profile registry must contain at least one profile.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        var profiles = new List<ProfileDefinition>(document.Profiles.Count);

        foreach (var dto in document.Profiles)
        {
            var id = ParseId(dto.Id, "profile.id");
            if (!keys.Add(id.Value))
            {
                throw new InvalidDataException($"Profile id '{id}' is declared more than once or collides with an alias.");
            }

            if (dto.Order is not { } order || order < 0 || !orders.Add(order))
            {
                throw new InvalidDataException($"Profile '{id}' must have a unique, non-negative order.");
            }

            var aliases = (dto.Aliases ?? []).Select(alias => ParseId(alias, $"profile[{id}].aliases")).ToArray();
            foreach (var alias in aliases)
            {
                if (!keys.Add(alias.Value))
                {
                    throw new InvalidDataException($"Alias '{alias}' of profile '{id}' collides with another profile id or alias.");
                }
            }

            var status = dto.Status switch
            {
                "active" => ProfileStatus.Active,
                "planned" => ProfileStatus.Planned,
                "retired" => ProfileStatus.Retired,
                _ => throw new InvalidDataException($"Profile '{id}' has unsupported status '{dto.Status}'.")
            };

            var label = ParseText(dto.Label, $"profile[{id}].label")
                ?? throw new InvalidDataException($"Profile '{id}' is missing a label.");
            var description = ParseText(dto.Description, $"profile[{id}].description");
            var reasonLabel = ParseText(dto.ReasonLabel, $"profile[{id}].reasonLabel");

            if (status == ProfileStatus.Active)
            {
                if (description is null || reasonLabel is null)
                {
                    throw new InvalidDataException($"Active profile '{id}' requires a description and a reasonLabel.");
                }

                if (!LegacyFlags.ContainsKey(id.Value))
                {
                    throw new InvalidDataException(
                        $"Active profile '{id}' cannot be activated before the UserProfile enum is removed (ADR-0003 step 4).");
                }
            }

            profiles.Add(new ProfileDefinition(id, status, order, dto.Icon, aliases, label, description, reasonLabel));
        }

        return new ProfileRegistry(profiles
            .OrderBy(static profile => profile.Order)
            .ToArray());
    }

    private static ProfileId ParseId(string? value, string field) =>
        ProfileId.IsValid(value)
            ? new ProfileId(value!)
            : throw new InvalidDataException($"{field} '{value}' is not a valid lowercase kebab-case profile id.");

    private static LocalizedText? ParseText(Dictionary<string, string>? values, string field)
    {
        if (values is null)
        {
            return null;
        }

        if (!values.TryGetValue(LocalizedText.DefaultLanguage, out var english) || string.IsNullOrWhiteSpace(english))
        {
            throw new InvalidDataException($"{field} requires a non-empty '{LocalizedText.DefaultLanguage}' value.");
        }

        if (values.Any(static pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new InvalidDataException($"{field} contains an empty language tag or value.");
        }

        return new LocalizedText(new Dictionary<string, string>(values, StringComparer.Ordinal));
    }

    private sealed class RegistryDto
    {
        public string? SchemaVersion { get; init; }
        public List<ProfileDto>? Profiles { get; init; }
    }

    private sealed class ProfileDto
    {
        public string? Id { get; init; }
        public string? Status { get; init; }
        public int? Order { get; init; }
        public string? Icon { get; init; }
        public List<string>? Aliases { get; init; }
        public Dictionary<string, string>? Label { get; init; }
        public Dictionary<string, string>? Description { get; init; }
        public Dictionary<string, string>? ReasonLabel { get; init; }
    }
}
