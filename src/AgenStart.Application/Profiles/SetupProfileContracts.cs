namespace AgenStart.Application.Profiles;

public sealed record SetupProfileApplication(
    string ApplicationId,
    string? Reason = null);

public sealed record SetupProfileMetadata(
    string? Name = null,
    string? AgenStartVersion = null);

public sealed record SetupProfileDocument(
    string Kind,
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string ProfileId,
    IReadOnlyList<SetupProfileApplication> Applications,
    SetupProfileMetadata? Metadata = null)
{
    public const string CurrentKind = "agenstart.setup";
    public const int CurrentSchemaVersion = 1;
}

public sealed record SetupProfileValidationError(
    string Code,
    string Message,
    string? Path = null);

public sealed record SetupProfileReadResult(
    bool IsValid,
    SetupProfileDocument? Profile,
    IReadOnlyList<SetupProfileValidationError> Errors)
{
    public static SetupProfileReadResult Valid(SetupProfileDocument profile) =>
        new(true, profile, Array.Empty<SetupProfileValidationError>());

    public static SetupProfileReadResult Invalid(params SetupProfileValidationError[] errors) =>
        new(false, null, errors);

    public static SetupProfileReadResult Invalid(IReadOnlyList<SetupProfileValidationError> errors) =>
        new(false, null, errors);
}

public sealed class SetupProfileValidationException : Exception
{
    public SetupProfileValidationException(IReadOnlyList<SetupProfileValidationError> errors)
        : base("The AgenStart setup profile is invalid.")
    {
        Errors = errors;
    }

    public IReadOnlyList<SetupProfileValidationError> Errors { get; }
}
