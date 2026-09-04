using AgenStart.Application.Profiles;

namespace AgenStart.Application.Tests;

public sealed class SetupProfileSerializerTests
{
    private readonly SetupProfileSerializer _serializer = new();

    [Fact]
    public void SerializeAndDeserialize_RoundTripsPortableSetupProfile()
    {
        var profile = new SetupProfileDocument(
            SetupProfileDocument.CurrentKind,
            SetupProfileDocument.CurrentSchemaVersion,
            new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            "development",
            [
                new SetupProfileApplication("git", "Essential for source control."),
                new SetupProfileApplication("visual-studio-code", "Recommended for development."),
                new SetupProfileApplication("7zip")
            ],
            new SetupProfileMetadata("Development setup", "0.1.0-alpha"));

        var json = _serializer.Serialize(profile);
        var result = _serializer.Deserialize(json);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Profile);
        Assert.Equal("development", result.Profile.ProfileId);
        Assert.Equal(3, result.Profile.Applications.Count);
        Assert.Equal("git", result.Profile.Applications[0].ApplicationId);
        Assert.Equal("Development setup", result.Profile.Metadata?.Name);
        Assert.False(json.Contains("hostname", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("serial", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("macAddress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedSchemaVersion()
    {
        const string json = """
        {
          "kind": "agenstart.setup",
          "schemaVersion": 99,
          "createdAtUtc": "2026-09-04T12:00:00+00:00",
          "profileId": "development",
          "applications": [
            { "applicationId": "git" }
          ]
        }
        """;

        var result = _serializer.Deserialize(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "profile.schema.unsupported");
    }

    [Fact]
    public void Deserialize_RejectsDuplicateCanonicalApplicationIds()
    {
        const string json = """
        {
          "kind": "agenstart.setup",
          "schemaVersion": 1,
          "createdAtUtc": "2026-09-04T12:00:00+00:00",
          "profileId": "development",
          "applications": [
            { "applicationId": "git" },
            { "applicationId": "git" }
          ]
        }
        """;

        var result = _serializer.Deserialize(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "profile.application_id.duplicate");
    }

    [Fact]
    public void Deserialize_RejectsNonCanonicalUppercaseApplicationId()
    {
        const string json = """
        {
          "kind": "agenstart.setup",
          "schemaVersion": 1,
          "createdAtUtc": "2026-09-04T12:00:00+00:00",
          "profileId": "development",
          "applications": [
            { "applicationId": "Git" }
          ]
        }
        """;

        var result = _serializer.Deserialize(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "profile.application_id.invalid");
    }

    [Fact]
    public void Deserialize_RejectsUnknownFieldsInsteadOfIgnoringThem()
    {
        const string json = """
        {
          "kind": "agenstart.setup",
          "schemaVersion": 1,
          "createdAtUtc": "2026-09-04T12:00:00+00:00",
          "profileId": "development",
          "applications": [
            { "applicationId": "git" }
          ],
          "hostname": "should-never-be-accepted"
        }
        """;

        var result = _serializer.Deserialize(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "profile.document.invalid_json");
    }

    [Fact]
    public void Deserialize_RejectsMalformedJsonCleanly()
    {
        var result = _serializer.Deserialize("{ not-valid-json");

        Assert.False(result.IsValid);
        Assert.Null(result.Profile);
        Assert.Contains(result.Errors, error => error.Code == "profile.document.invalid_json");
    }

    [Fact]
    public void Serialize_RejectsEmptyApplicationSelection()
    {
        var profile = new SetupProfileDocument(
            SetupProfileDocument.CurrentKind,
            SetupProfileDocument.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            "development",
            []);

        var exception = Assert.Throws<SetupProfileValidationException>(() => _serializer.Serialize(profile));

        Assert.Contains(exception.Errors, error => error.Code == "profile.applications.empty");
    }
}
