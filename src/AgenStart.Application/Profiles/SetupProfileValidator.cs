using System.Text.RegularExpressions;

namespace AgenStart.Application.Profiles;

public sealed partial class SetupProfileValidator
{
    public const int MaxApplications = 200;
    public const int MaxReasonLength = 512;
    public const int MaxProfileNameLength = 100;
    public const int MaxVersionLength = 64;

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

        if (profile.SchemaVersion != SetupProfileDocument.CurrentSchemaVersion)
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

        if (string.IsNullOrWhiteSpace(profile.ProfileId) || !PortableIdPattern().IsMatch(profile.ProfileId))
        {
            errors.Add(new SetupProfileValidationError(
                "profile.profile_id.invalid",
                "profileId must be a portable lowercase identifier using letters, digits, dots, underscores or hyphens.",
                "profileId"));
        }

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

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex PortableIdPattern();
}
