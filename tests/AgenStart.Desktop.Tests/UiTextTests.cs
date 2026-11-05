using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgenStart.Core.Recommendations;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.Tests;

public sealed partial class UiTextTests
{
    [Fact]
    public void Key_text_is_resolved_again_after_a_language_switch()
    {
        var localizer = new FileLocalizer("en");
        var text = UiText.Key("Installation.Status.Retrying", "Git");

        Assert.Equal("Retrying Git.", text.Resolve(localizer));
        localizer.Switch("fr");
        Assert.Equal("Nouvelle tentative pour Git.", text.Resolve(localizer));
    }

    [Fact]
    public void Nested_units_follow_the_language_and_culture()
    {
        var localizer = new FileLocalizer("fr");
        var text = UiText.Key("Installation.Item.Downloaded", UiUnits.Bytes(1536L * 1024 * 1024));

        Assert.Equal("1,5 Go téléchargés", text.Resolve(localizer));
        localizer.Switch("en");
        Assert.Equal("1.5 GB downloaded", text.Resolve(localizer));
    }

    [Theory]
    [InlineData("fr", 0, "0 application")]
    [InlineData("fr", 1, "1 application")]
    [InlineData("fr", 3, "3 applications")]
    [InlineData("en", 0, "0 apps")]
    [InlineData("en", 1, "1 app")]
    public void Plural_text_uses_each_language_rule(string language, int count, string expected) =>
        Assert.Equal(expected, UiText.Plural("Recommendations.Summary.Apps", count).Resolve(new FileLocalizer(language)));

    [Fact]
    public void Raw_and_dynamic_text_bypass_the_dictionaries()
    {
        var localizer = new FileLocalizer("fr");

        Assert.Equal("Installation.Status.Ready", UiText.Raw("Installation.Status.Ready").Resolve(localizer));
        Assert.Equal("fr", UiText.Dynamic(text => text.Language).Resolve(localizer));
    }

    [Fact]
    public void Every_pipeline_stage_shown_to_the_user_has_an_increasing_percentage()
    {
        var steps = Enum.GetValues<RecommendationPipelineStage>()
            .Select(RecommendationProgressMap.For)
            .OfType<RecommendationProgressStep>()
            .Prepend(RecommendationProgressMap.Starting)
            .Append(RecommendationProgressMap.Ready)
            .ToArray();

        Assert.True(steps.Length >= 6);
        Assert.Equal(steps.Select(step => step.Percent).Order(), steps.Select(step => step.Percent));
    }

    [Fact]
    public void Every_string_key_used_in_desktop_code_exists_in_english()
    {
        // Keys are always written as whole literals in C# so this scan sees every one of them.
        var english = FileLocalizer.Load("en");
        var prefixes = english.Keys.Select(key => key[..key.IndexOf('.', StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);
        var root = LocalizationTests.DesktopSourceDirectory();
        var sources = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        var used = sources
            .SelectMany(path => KeyLiteral().Matches(File.ReadAllText(path))
                .Select(match => (File: Path.GetFileName(path), Key: match.Groups[1].Value)))
            .Where(reference => prefixes.Contains(reference.Key[..reference.Key.IndexOf('.', StringComparison.Ordinal)]))
            .ToArray();
        var missing = used
            .Where(reference => !english.ContainsKey(reference.Key)
                && !(english.ContainsKey(reference.Key + ".One") && english.ContainsKey(reference.Key + ".Other")))
            .Select(reference => $"{reference.File}: {reference.Key}")
            .Distinct()
            .ToArray();

        Assert.True(used.Length > 100, $"Only {used.Length} key literals found; the scan pattern is probably broken.");
        Assert.Empty(missing);
    }

    [GeneratedRegex("\"([A-Z][A-Za-z0-9]*(?:\\.[A-Z][A-Za-z0-9]*)+)\"")]
    private static partial Regex KeyLiteral();

    /// <summary>Reads the shipped dictionaries with the same English fallback as the app.</summary>
    private sealed class FileLocalizer(string language) : LocalizerBase
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        private readonly Dictionary<string, string> _english = Load("en");
        private Dictionary<string, string> _current = Load(language);
        private string _language = language;

        public override string Language => _language;

        public override string this[string key] =>
            _current.TryGetValue(key, out var value) || _english.TryGetValue(key, out value) ? value : key;

        public void Switch(string next)
        {
            _language = next;
            _current = Load(next);
            RaiseLanguageChanged();
        }

        public static Dictionary<string, string> Load(string name) =>
            XDocument.Load(Path.Combine(LocalizationTests.DesktopSourceDirectory(), "Strings", $"{name}.axaml"))
                .Root!
                .Elements(Xaml + "String")
                .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Value, StringComparer.Ordinal);
    }
}
