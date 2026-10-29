using System.Text.RegularExpressions;
using AgenStart.Core.Profiles;

namespace AgenStart.Application.Profiles;

public sealed partial class SetupProfileValidator
{
    public const int MaxApplications = 200;
    public const int MaxReasonLength = 512;
    public const int MaxProfileNameLength = 100;
    public const int MaxVersionLength = 64;
    public const int MaxProfileIds = 32;

    private readonly ProfileRegistry _profiles;

    public SetupProfileValidator(ProfileRegistry? profiles = null)
    {
        _profiles = profiles ?? ProfileRegistry.Default;
    }

    public IReadOnlyList<SetupProfileValidationError> Validate(SetupProfileDocument? profile)
    {
        if (profile is null)
        {
            return [new SetupProfileValidationError(
                "profile.missing",
                "The setup profile document is missing.")];
        }

        var errors = new List<SetupProfileValidationError>();

        if (!string.Equals(profile.Kind, SetupProfileDocument.CurrentKind, StringComparison.Ordinal))
        {
            errors.Add(new SetupProfileValidationError(
                "profile.kind.unsupported",
                $"Expected kind '{SetupProfileDocument.CurrentKind}'.",
                "kind"));
        }

        if (profile.SchemaVersion < SetupProfileDocument.MinimumSupportedSchemaVersion ||
            profile.SchemaVersion > SetupProfileDocument.CurrentSchemaVersion)
        {
            errors.Add(new SetupProfileValidationError(
                "profile.schema.unsupported",
                $"Schema version {profile.SchemaVersion} is not supported by this AgenStart build.",
                "schemaVersion"));
        }

        if (profile.CreatedAtUtc == default)
        {
            errors.Add(new SetupProfileValidationError(
                "profile.created_at.invalid",
                "createdAtUtc must be a valid timestamp.",
                "createdAtUtc"));
        }

        ValidateProfileSelection(profile, errors);

        if (profile.Applications is null || profile.Applications.Count == 0)
        {
            errors.Add(new SetupProfileValidationError(
                "profile.applications.empty",
                "A setup profile must contain at least one application.",
                "applications"));
        }
        else if (profile.Applications.Count > MaxApplications)
        {
            errors.Add(new SetupProfileValidationError(
                "profile.applications.too_many",
                $"A setup profile can contain at most {MaxApplications} applications.",
                "applications"));
        }

        if (profile.Applications is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < profile.Applications.Count; index++)
            {
                var application = profile.Applications[index];
                var path = $"applications[{index}]";
                if (application is null)
                {
                    errors.Add(new SetupProfileValidationError(
                        "profile.application.missing",
                        "Application entries cannot be null.",
                        path));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(application.ApplicationId) ||
                    !PortableIdPattern().IsMatch(application.ApplicationId))
                {
                    errors.Add(new SetupProfileValidationError(
                        "profile.application_id.invalid",
                        "applicationId must be a portable canonical AgenStart application identifier.",
                        path + ".applicationId"));
                }
                else if (!seen.Add(application.ApplicationId))
                {
                    errors.Add(new SetupProfileValidationError(
                        "profile.application_id.duplicate",
                        $"Application '{application.ApplicationId}' appears more than once.",
                        path + ".applicationId"));
                }

                if (application.Reason is { Length: > MaxReasonLength })
                {
                    errors.Add(new SetupProfileValidationError(
                        "profile.reason.too_long",
                        $"Recommendation context cannot exceed {MaxReasonLength} characters.",
                        path + ".reason"));
                }
            }
        }

        if (profile.Metadata is not null)
        {
            if (profile.Metadata.Name is { } name &&
                (string.IsNullOrWhiteSpace(name) || name.Length > MaxProfileNameLength))
            {
                errors.Add(new SetupProfileValidationError(
                    "profile.metadata.name.invalid",
                    $"Profile name must be between 1 and {MaxProfileNameLength} characters when provided.",
                    "metadata.name"));
            }

            if (profile.Metadata.AgenStartVersion is { } version &&
                (string.IsNullOrWhiteSpace(version) || version.Length > MaxVersionLength))
            {
                errors.Add(new SetupProfileValidationError(
                    "profile.metadata.version.invalid",
                    $"AgenStart version cannot exceed {MaxVersionLength} characters.",
                    "metadata.agenStartVersion"));
            }
        }

        return errors;
    }

    public SetupProfileDocument Normalize(SetupProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var ids = CollectRawIds(profile)
            .Select(NormalizeProfileToken)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return profile with
        {
            SchemaVersion = Math.Max(profile.SchemaVersion, SetupProfileDocument.CurrentSchemaVersion),
            ProfileId = ids.Length > 0 ? ids[0] : profile.ProfileId,
            ProfileIds = ids
        };
    }

    private void ValidateProfileSelection(
        SetupProfileDocument profile,
        List<SetupProfileValidationError> errors)
    {
        var rawIds = CollectRawIds(profile);
        if (rawIds.Count == 0)
        {
            errors.Add(new SetupProfileValidationError(
                profile.SchemaVersion >= 2 ? "profile.profile_ids.empty" : "profile.profile_id.invalid",
                profile.SchemaVersion >= 2
                    ? "profileIds must contain at least one usage profile."
                    : "profileId must be a portable lowercase identifier using letters, digits, dots, underscores or hyphens.",
                profile.SchemaVersion >= 2 ? "profileIds" : "profileId"));
            return;
        }

        if (rawIds.Count > MaxProfileIds)
        {
            errors.Add(new SetupProfileValidationError(
                "profile.profile_ids.too_many",
                $"A setup profile can list at most {MaxProfileIds} usage profiles.",
                "profileIds"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rawIds.Count; index++)
        {
            var token = rawIds[index];
            var path = profile.ProfileIds is { Count: > 0 } ? $"profileIds[{index}]" : "profileId";
            if (!PortableIdPattern().IsMatch(token))
            {
                errors.Add(new SetupProfileValidationError(
                    "profile.profile_id.invalid",
                    "Each usage profile id must be a portable lowercase identifier using letters, digits, dots, underscores or hyphens.",
                    path));
                continue;
            }

            var canonical = NormalizeProfileToken(token);
            if (!seen.Add(canonical))
            {
                continue;
            }

            if (!_profiles.TryResolve(token, out _))
            {
                errors.Add(new SetupProfileValidationError(
                    "profile.profile_id.unknown",
                    $"Usage profile '{token}' is not in the AgenStart profile registry.",
                    path));
            }
        }
    }

    private string NormalizeProfileToken(string token)
    {
        var trimmed = token.Trim().ToLowerInvariant();
        return _profiles.TryResolve(trimmed, out var profile) ? profile.Id.Value : trimmed;
    }

    private static IReadOnlyList<string> CollectRawIds(SetupProfileDocument profile)
    {
        if (profile.ProfileIds is { Count: > 0 })
        {
            return profile.ProfileIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(profile.ProfileId)
            ? []
            : [profile.ProfileId.Trim()];
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex PortableIdPattern();
}
