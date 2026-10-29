using System.Text.RegularExpressions;

namespace AgenStart.Core.Profiles;

public readonly partial record struct ProfileId
{
    public ProfileId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid profile id. Use lowercase kebab-case (2-40 characters).",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    public override string ToString() => Value ?? string.Empty;

    [GeneratedRegex("^[a-z][a-z0-9-]{1,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

public enum ProfileStatus
{
    Active,
    Planned,
    Retired
}

public sealed record LocalizedText(IReadOnlyDictionary<string, string> Values)
{
    public const string DefaultLanguage = "en";

    public string En => Values[DefaultLanguage];

    public string Get(string language) =>
        Values.TryGetValue(language, out var value) ? value : En;
}

public sealed record ProfileDefinition(
    ProfileId Id,
    ProfileStatus Status,
    int Order,
    string? Icon,
    IReadOnlyList<ProfileId> Aliases,
    LocalizedText Label,
    LocalizedText? Description,
    LocalizedText? ReasonLabel)
{
    public bool IsActive => Status == ProfileStatus.Active;
}
