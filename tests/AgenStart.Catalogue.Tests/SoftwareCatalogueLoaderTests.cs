using System.Text.Json.Nodes;
using AgenStart.Core.Catalogue;
using AgenStart.PackageManagement;
using Xunit;

namespace AgenStart.Catalogue.Tests;

public sealed class SoftwareCatalogueLoaderTests
{
    [Fact]
    public void Load_real_fixture_maps_runtime_catalogue()
    {
        var catalogue = LoadFixture();

        Assert.Equal("1.0.0", catalogue.SchemaVersion);
        Assert.Equal("0.2.0", catalogue.CatalogueVersion);
        Assert.Equal(39, catalogue.Applications.Count);

        var git = Assert.Single(catalogue.Applications, application => application.Id == "git");
        Assert.Equal("Git Project", git.Publisher);
        Assert.NotNull(git.WindowsPackage);
        Assert.Equal(PackageProviderIds.WinGet, git.WindowsPackage.ProviderId);
        Assert.Equal("Git.Git", git.WindowsPackage.PackageId);
        Assert.Equal("winget", git.WindowsPackage.Source);
    }

    [Fact]
    public void Load_supports_six_profiles_and_gem_recommendations()
    {
        var catalogue = LoadFixture();
        var recommendations = catalogue.Definitions.SelectMany(application => application.Recommendations).ToArray();

        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Personal);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Business);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Learning);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Development);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Creative);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Gaming);
        Assert.Contains(recommendations, rule => rule.Level == RecommendationLevel.Gem);
    }

    [Fact]
    public void Load_enforces_plain_descriptions_for_every_catalogue_app()
    {
        var catalogue = LoadFixture();

        Assert.All(catalogue.Applications, application =>
        {
            Assert.False(string.IsNullOrWhiteSpace(application.Description));
            Assert.InRange(application.Description.Length, 1, 180);
        });
    }

    [Fact]
    public void Load_builds_detection_targets_from_same_canonical_identity()
    {
        var catalogue = LoadFixture();
        var applications = catalogue.Applications.ToDictionary(application => application.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var target in catalogue.DetectionTargets)
        {
            var application = applications[target.ApplicationId];
            Assert.Equal(application.Name, target.DisplayName);
            Assert.Equal(application.Publisher, target.Publisher);
            Assert.Equal(application.ProviderPackages, target.ProviderPackages);
        }
    }

    [Fact]
    public void Load_rejects_duplicate_application_ids()
    {
        var document = LoadFixtureNode();
        var applications = document["applications"]!.AsArray();
        applications.Add(JsonNode.Parse(applications[0]!.ToJsonString()));

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("Duplicate catalogue application id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_untrusted_winget_source()
    {
        var document = LoadFixtureNode();
        var firstApplication = document["applications"]!.AsArray()[0]!.AsObject();
        firstApplication["providers"]!.AsArray()[0]!["source"] = "random-mirror";

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("Untrusted WinGet source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_unknown_architecture()
    {
        var document = LoadFixtureNode();
        var firstApplication = document["applications"]!.AsArray()[0]!.AsObject();
        firstApplication["requirements"]!["minimum"]!["architectures"]![0] = "mips";

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("Unsupported architecture", exception.Message, StringComparison.Ordinal);
    }

    private static SoftwareCatalogue LoadFixture()
    {
        using var stream = File.OpenRead(FixturePath());
        return new SoftwareCatalogueLoader().Load(stream);
    }

    private static JsonObject LoadFixtureNode() =>
        JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalogue.fixtures.json");
}
