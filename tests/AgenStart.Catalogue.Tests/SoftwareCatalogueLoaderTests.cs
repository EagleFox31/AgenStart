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
        Assert.Equal("0.3.0", catalogue.CatalogueVersion);
        Assert.Equal(62, catalogue.Applications.Count);

        var git = Assert.Single(catalogue.Applications, application => application.Id == "git");
        Assert.Equal("Git Project", git.Publisher);
        Assert.NotNull(git.WindowsPackage);
        Assert.Equal(PackageProviderIds.WinGet, git.WindowsPackage.ProviderId);
        Assert.Equal("Git.Git", git.WindowsPackage.PackageId);
        Assert.Equal("winget", git.WindowsPackage.Source);

        var termius = Assert.Single(catalogue.Applications, application => application.Id == "termius");
        Assert.Equal("Termius Corporation", termius.Publisher);
        Assert.NotNull(termius.WindowsPackage);
        Assert.Equal(PackageProviderIds.WinGet, termius.WindowsPackage.ProviderId);
        Assert.Equal("Termius.Termius", termius.WindowsPackage.PackageId);
        Assert.Equal("winget", termius.WindowsPackage.Source);

        var nodejs = Assert.Single(catalogue.Applications, application => application.Id == "nodejs");
        Assert.Equal("OpenJS.NodeJS.LTS", nodejs.WindowsPackage!.PackageId);
        Assert.Contains(nodejs.Definition.Recommendations, rule => rule.Profile == UserProfile.Development);

        var postgresql = Assert.Single(catalogue.Applications, application => application.Id == "postgresql");
        Assert.Equal("PostgreSQL.PostgreSQL.17", postgresql.WindowsPackage!.PackageId);

        var chrome = Assert.Single(catalogue.Applications, application => application.Id == "google-chrome");
        Assert.Equal("Google.Chrome", chrome.WindowsPackage!.PackageId);

        var python = Assert.Single(catalogue.Applications, application => application.Id == "python");
        Assert.Equal("Python.Python.3.13", python.WindowsPackage!.PackageId);

        var kodi = Assert.Single(catalogue.Applications, application => application.Id == "kodi");
        Assert.Equal("XBMCFoundation.Kodi", kodi.WindowsPackage!.PackageId);
        Assert.Contains(kodi.Definition.Recommendations, rule => rule.Profile == UserProfile.Entertainment);

        var recommendation = Assert.Single(termius.Definition.Recommendations);
        Assert.Equal(UserProfile.Development, recommendation.Profile);
        Assert.Equal(RecommendationLevel.Recommended, recommendation.Level);
        Assert.Equal("development.remote-infrastructure", recommendation.ReasonKey);
    }

    [Fact]
    public void Load_supports_active_profiles_and_gem_recommendations()
    {
        var catalogue = LoadFixture();
        var recommendations = catalogue.Definitions.SelectMany(application => application.Recommendations).ToArray();

        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Personal);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Business);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Learning);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Development);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Creative);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Gaming);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.Entertainment);
        Assert.Contains(recommendations, rule => rule.Profile == UserProfile.ItStudent);
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

    [Fact]
    public void Load_rejects_unknown_profile()
    {
        var document = LoadFixtureNode();
        FirstRuleFor(document, "personal")["profile"] = "not-a-profile";

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("Unsupported profile 'not-a-profile'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_legacy_profile_alias_in_catalogue()
    {
        var document = LoadFixtureNode();
        FirstRuleFor(document, "creative")["profile"] = "creation";

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("legacy alias; use 'creative'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_rules_for_planned_profiles()
    {
        var document = LoadFixtureNode();
        FirstRuleFor(document, "personal")["profile"] = "family";

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("is planned, not active", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_active_profile_without_any_recommendation()
    {
        var document = LoadFixtureNode();
        foreach (var application in document["applications"]!.AsArray())
        {
            var rules = application!["recommendations"]!.AsArray();
            foreach (var rule in rules.Where(rule => (string?)rule!["profile"] == "gaming").ToArray())
            {
                rules.Remove(rule);
            }
        }

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SoftwareCatalogueLoader().Load(document.ToJsonString()));

        Assert.Contains("Active profile 'gaming' has no application recommendation", exception.Message, StringComparison.Ordinal);
    }

    private static JsonObject FirstRuleFor(JsonObject document, string profile) =>
        document["applications"]!.AsArray()
            .SelectMany(application => application!["recommendations"]!.AsArray())
            .First(rule => (string?)rule!["profile"] == profile)!
            .AsObject();

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
