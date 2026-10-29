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
        Assert.Equal(2, result.Profile.SchemaVersion);
        Assert.Equal("development", result.Profile.ProfileId);
        Assert.Equal(["development"], result.Profile.ProfileIds);
        Assert.Contains("\"profileIds\"", json, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("personal", "personal")]
    [InlineData("development", "development")]
    [InlineData("business", "business")]
    [InlineData("creative", "creative")]
    [InlineData("learning", "learning")]
    [InlineData("gaming", "gaming")]
    [InlineData("entertainment", "entertainment")]
    [InlineData("it-student", "it-student")]
    [InlineData("creation", "creative")]
    [InlineData("training", "learning")]
    [InlineData("study", "learning")]
    [InlineData("etudiant-it", "it-student")]
    [InlineData("student-it", "it-student")]
    public void Deserialize_AcceptsEveryKnownV1ProfileIdIncludingLegacyAliases(string profileId, string canonical)
    {
        var json = $$"""
        {
          "kind": "agenstart.setup",
          "schemaVersion": 1,
          "createdAtUtc": "2026-09-04T12:00:00+00:00",
          "profileId": "{{profileId}}",
          "applications": [
            { "applicationId": "7zip" }
          ]
        }
        """;

        var result = _serializer.Deserialize(json);

        Assert.True(result.IsValid);
        Assert.Equal(canonical, result.Profile?.ProfileId);
        Assert.Equal([canonical], result.Profile?.ProfileIds);
    }

    [Fact]
    public void SerializeAndDeserialize_RoundTripsMultipleCanonicalProfileIds()
    {
        var profile = new SetupProfileDocument(
            SetupProfileDocument.CurrentKind,
            SetupProfileDocument.CurrentSchemaVersion,
            new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero),
            "personal",
            [new SetupProfileApplication("7zip")],
            new SetupProfileMetadata("Mixed setup", "0.4.1"),
            ["personal", "development", "creation"]);

        var json = _serializer.Serialize(profile);
        var result = _serializer.Deserialize(json);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Profile!.SchemaVersion);
        Assert.Equal(["personal", "development", "creative"], result.Profile.ProfileIds);
        Assert.Equal("personal", result.Profile.ProfileId);
        Assert.DoesNotContain("creation", result.Profile.ProfileIds!);
    }

    [Fact]
    public void Deserialize_RejectsUnknownProfileId()
    {
        const string json = """
        {
          "kind": "agenstart.setup",
          "schemaVersion": 2,
          "createdAtUtc": "2026-09-04T12:00:00+00:00",
          "profileIds": ["not-a-profile"],
          "applications": [
            { "applicationId": "7zip" }
          ]
        }
        """;

        var result = _serializer.Deserialize(json);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "profile.profile_id.unknown");
    }

    [Fact]
    public void Serialize_RejectsFlagsEnumTextProducedByMultiProfileSelection()
    {
        var profile = new SetupProfileDocument(
            SetupProfileDocument.CurrentKind,
            SetupProfileDocument.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            "personal, development",
            [new SetupProfileApplication("7zip")]);

        var exception = Assert.Throws<SetupProfileValidationException>(() => _serializer.Serialize(profile));

        Assert.Contains(exception.Errors, error => error.Code == "profile.profile_id.invalid");
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
