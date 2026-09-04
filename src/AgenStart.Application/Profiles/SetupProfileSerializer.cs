using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenStart.Application.Profiles;

public sealed class SetupProfileSerializer
{
    public const int MaxDocumentBytes = 256 * 1024;

    private readonly SetupProfileValidator _validator;
    private readonly JsonSerializerOptions _options;

    public SetupProfileSerializer(SetupProfileValidator? validator = null)
    {
        _validator = validator ?? new SetupProfileValidator();
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string Serialize(SetupProfileDocument profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = _validator.Validate(profile);
        if (errors.Count > 0)
        {
            throw new SetupProfileValidationException(errors);
        }

        return JsonSerializer.Serialize(profile, _options);
    }

    public SetupProfileReadResult Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SetupProfileReadResult.Invalid(new SetupProfileValidationError(
                "profile.document.empty",
                "The setup profile file is empty."));
        }

        if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
        {
            return SetupProfileReadResult.Invalid(new SetupProfileValidationError(
                "profile.document.too_large",
                $"The setup profile exceeds the {MaxDocumentBytes / 1024} KB safety limit."));
        }

        SetupProfileDocument? profile;
        try
        {
            profile = JsonSerializer.Deserialize<SetupProfileDocument>(json, _options);
        }
        catch (JsonException)
        {
            return SetupProfileReadResult.Invalid(new SetupProfileValidationError(
                "profile.document.invalid_json",
                "The setup profile is malformed or contains unsupported fields."));
        }
        catch (NotSupportedException)
        {
            return SetupProfileReadResult.Invalid(new SetupProfileValidationError(
                "profile.document.unsupported",
                "The setup profile contains a value that this AgenStart build cannot read."));
        }

        var errors = _validator.Validate(profile);
        return errors.Count == 0 && profile is not null
            ? SetupProfileReadResult.Valid(profile)
            : SetupProfileReadResult.Invalid(errors);
    }
}
