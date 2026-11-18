using System.Text.Json;
using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

public sealed class UninstallRuleSetLoaderTests
{
    private const string Minimal = """
        {
          "schemaVersion": "1.0",
          "rulesVersion": "test.1",
          "genericRules": [
            { "id": "generic.product", "kind": "directory", "roots": ["{LocalAppData}"],
              "relativePath": "{Publisher}\\{Product}", "dataClass": "appData", "confidence": 6 }
          ]
        }
        """;

    [Fact]
    public void LoadsTheShippedRuleFile()
    {
        using var stream = File.OpenRead(TestRules.ShippedRulesPath());
        var rules = UninstallRuleSetLoader.Load(stream);

        Assert.Equal("1.0", rules.SchemaVersion);
        Assert.NotEmpty(rules.GenericRules);
        Assert.NotEmpty(rules.ProductRules);
        Assert.Contains(rules.GenericRules, rule => rule.Kind == LeftoverKind.Shortcut);
    }

    [Fact]
    public void TheFrozenGoldenCopyStillMatchesTheShippedFile()
    {
        // When the shipped rules change, refresh the frozen copy and the golden plans together.
        Assert.Equal(
            File.ReadAllText(TestRules.ShippedRulesPath()).ReplaceLineEndings("\n"),
            File.ReadAllText(Path.Combine(TestRules.GoldenDirectory(), "uninstall-rules-2026.11.0.frozen.json")).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheShippedSchemaPinsTheSupportedVersionAndClosesEveryObject()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(TestRules.ShippedSchemaPath()));
        var root = schema.RootElement;

        Assert.Equal(UninstallRuleSetLoader.SupportedSchemaVersion, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
        foreach (var obj in Objects(root))
        {
            Assert.True(
                obj.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False,
                "Every object in the schema must refuse unknown fields.");
        }
    }

    [Fact]
    public void LoadsAMinimalRuleSetWithDefaults()
    {
        var rules = UninstallRuleSetLoader.Load(Minimal);

        Assert.Equal("test.1", rules.RulesVersion);
        Assert.Empty(rules.ProductRules);
        Assert.Equal(VolumeThresholds.Default, rules.VolumeThresholds);
        Assert.Equal(@"{Publisher}\{Product}", rules.GenericRules[0].RelativePath);
    }

    [Theory]
    [InlineData("\"schemaVersion\": \"1.0\"", "\"schemaVersion\": \"2.0\"")]
    [InlineData("\"schemaVersion\": \"1.0\",", "")]
    [InlineData("\"rulesVersion\": \"test.1\",", "\"rulesVersion\": \"test.1\", \"unknownField\": true,")]
    [InlineData("\"confidence\": 6", "\"confidence\": 6, \"note\": \"x\"")]
    [InlineData("{Publisher}\\\\{Product}", "{Vendor}\\\\{Product}")]
    [InlineData("{Publisher}\\\\{Product}", "{Publisher}x\\\\{Product}")]
    [InlineData("{Publisher}\\\\{Product}", "..\\\\{Product}")]
    [InlineData("\"{LocalAppData}\"", "\"{Windows}\"")]
    [InlineData("\"{LocalAppData}\"", "\"C:\\\\Temp\"")]
    [InlineData("\"kind\": \"directory\"", "\"kind\": \"scheduledTask\"")]
    [InlineData("\"kind\": \"directory\"", "\"kind\": \"Directory\"")]
    [InlineData("\"dataClass\": \"appData\"", "\"dataClass\": \"secrets\"")]
    [InlineData("\"confidence\": 6", "\"confidence\": 21")]
    [InlineData("\"generic.product\"", "\"Generic Product\"")]
    [InlineData("\"schemaVersion\"", "\"SchemaVersion\"")]
    [InlineData("\"relativePath\": \"{Publisher}\\\\{Product}\",", "")]
    public void FailsClosedOnAnInvalidOrUnknownDocument(string original, string replacement)
    {
        Assert.Contains(original, Minimal, StringComparison.Ordinal);
        var json = Minimal.Replace(original, replacement, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => UninstallRuleSetLoader.Load(json));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    public void FailsClosedOnMalformedJson(string json)
    {
        Assert.Throws<InvalidDataException>(() => UninstallRuleSetLoader.Load(json));
    }

    [Theory]
    [InlineData("\"{Windows}\\\\System32\"")]
    [InlineData("\"{LocalAppData}\\\\{Product}\"")]
    [InlineData("\"HKCU\\\\Software\\\\{Publisher}\"")]
    [InlineData("\"relative\\\\path\"")]
    public void RejectsAnUnknownOrDynamicPlaceholderInForbiddenPaths(string path)
    {
        var json = Minimal.Replace("\"genericRules\"", $"\"extraForbiddenPaths\": [{path}], \"genericRules\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => UninstallRuleSetLoader.Load(json));
    }

    [Fact]
    public void RejectsDuplicatedRuleIds()
    {
        var json = Minimal.Replace(
            "\"genericRules\": [",
            "\"genericRules\": [ { \"id\": \"generic.product\", \"kind\": \"directory\", \"roots\": [\"{InstallLocation}\"], \"dataClass\": \"appData\", \"confidence\": 0 },",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => UninstallRuleSetLoader.Load(json));
    }

    [Fact]
    public void RejectsAProductLeftoverWhosePathDoesNotFitItsKind()
    {
        var json = Minimal.Replace(
            "\"genericRules\"",
            """
            "productRules": [ { "id": "vendor.app", "match": { "displayName": ["App"] },
              "leftovers": [ { "kind": "registryKey", "path": "{LocalAppData}\\App", "dataClass": "cache" } ] } ],
            "genericRules"
            """,
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => UninstallRuleSetLoader.Load(json));
    }

    [Fact]
    public void ConfigCanOnlyHardenTheSafetyFloor()
    {
        // The rule file has no way to express an "allowed" path: a stop word or forbidden path it adds
        // is merged with the built-in floor, and the floor itself is not read from the file.
        var json = Minimal.Replace("\"genericRules\"", "\"genericNameStoplist\": [\"acme\"], \"genericRules\"", StringComparison.Ordinal);
        var rules = UninstallRuleSetLoader.Load(json);
        var policy = PathSafetyPolicy.Create(new FakeKnownFolderResolver(), rules, null);

        Assert.False(policy.IsDistinctiveName("acme"));
        Assert.All(PathSafetyPolicy.BuiltInStoplist, word => Assert.False(policy.IsDistinctiveName(word)));
        Assert.Throws<InvalidDataException>(() => UninstallRuleSetLoader.Load(
            Minimal.Replace("\"genericRules\"", "\"allowedPaths\": [\"{ProgramData}\"], \"genericRules\"", StringComparison.Ordinal)));
    }

    private static IEnumerable<JsonElement> Objects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
            {
                yield return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in Objects(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in Objects(item))
                {
                    yield return nested;
                }
            }
        }
    }
}
