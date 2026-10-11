using System.Text.Json.Nodes;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Profiles;
using Xunit;

namespace AgenStart.Catalogue.Tests;

public sealed class ProfileRegistryTests
{
    [Fact]
    public void Shipped_registry_exposes_eight_active_profiles_in_canonical_order()
    {
        var registry = ProfileRegistry.Default;

        Assert.Equal(
            ["personal", "development", "business", "creative", "learning", "it-student", "gaming", "entertainment"],
            registry.Active.Select(profile => profile.Id.Value));
        Assert.All(registry.Active, profile =>
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.ReasonLabel?.En));
            Assert.False(string.IsNullOrWhiteSpace(profile.Label.Get("fr")));
        });
    }

    [Fact]
    public void Shipped_registry_keeps_family_and_later_specialist_profiles_planned()
    {
        var registry = ProfileRegistry.Default;

        Assert.True(registry.TryResolve("family", out var family));
        Assert.Equal(ProfileStatus.Planned, family.Status);
        Assert.True(registry.TryResolve("entertainment", out var entertainment));
        Assert.Equal(ProfileStatus.Active, entertainment.Status);
        Assert.Equal(
            ["family", "data-ai", "it-admin", "cloud-devops", "cybersecurity"],
            registry.Profiles
                .Where(profile => profile.Status == ProfileStatus.Planned)
                .Select(profile => profile.Id.Value));
    }

    [Theory]
    [InlineData("creation", "creative")]
    [InlineData("training", "learning")]
    [InlineData("study", "learning")]
    [InlineData("etudiant-it", "it-student")]
    [InlineData("student-it", "it-student")]
    [InlineData(" Gaming ", "gaming")]
    public void TryResolve_maps_ids_and_legacy_aliases_to_canonical_profiles(string input, string expected)
    {
        Assert.True(ProfileRegistry.Default.TryResolve(input, out var profile));
        Assert.Equal(expected, profile.Id.Value);
    }

    [Fact]
    public void Expand_returns_selected_profiles_in_registry_order()
    {
        var registry = ProfileRegistry.Default;

        var expanded = registry.Expand(UserProfile.Gaming | UserProfile.Personal | UserProfile.Learning);

        Assert.Equal(["personal", "learning", "gaming"], expanded.Select(profile => profile.Id.Value));
    }

    [Fact]
    public void Load_rejects_duplicate_profile_id()
    {
        var document = ShippedRegistryNode();
        document["profiles"]!.AsArray()[1]!["id"] = "personal";

        AssertRejected(document, "declared more than once");
    }

    [Fact]
    public void Load_rejects_alias_colliding_with_another_profile()
    {
        var document = ShippedRegistryNode();
        document["profiles"]!.AsArray()[0]!["aliases"] = new JsonArray("gaming");

        AssertRejected(document, "declared more than once");
    }

    [Fact]
    public void Load_rejects_activating_a_profile_without_legacy_mapping()
    {
        var document = ShippedRegistryNode();
        var family = document["profiles"]!.AsArray().Single(node => (string?)node!["id"] == "family")!;
        family["status"] = "active";
        family["reasonLabel"] = new JsonObject { ["en"] = "family use" };

        AssertRejected(document, "ADR-0003 step 4");
    }

    [Fact]
    public void Load_rejects_missing_english_label()
    {
        var document = ShippedRegistryNode();
        document["profiles"]!.AsArray()[0]!["label"] = new JsonObject { ["fr"] = "Personnel" };

        AssertRejected(document, "requires a non-empty 'en' value");
    }

    [Fact]
    public void Load_rejects_non_kebab_case_id()
    {
        var document = ShippedRegistryNode();
        document["profiles"]!.AsArray()[0]!["id"] = "Personal";

        AssertRejected(document, "not a valid lowercase kebab-case profile id");
    }

    [Fact]
    public void Load_rejects_unknown_fields()
    {
        var document = ShippedRegistryNode();
        document["profiles"]!.AsArray()[0]!["command"] = "winget install";

        AssertRejected(document, "unsupported fields");
    }

    private static void AssertRejected(JsonObject document, string expectedMessage)
    {
        var exception = Assert.Throws<InvalidDataException>(() => ProfileRegistry.Load(document.ToJsonString()));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static JsonObject ShippedRegistryNode()
    {
        using var stream = typeof(ProfileRegistry).Assembly.GetManifestResourceStream(ProfileRegistry.EmbeddedResourceName)!;
        return JsonNode.Parse(stream)!.AsObject();
    }
}
