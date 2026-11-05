using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.Tests;

public sealed partial class LocalizationTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("fr", "fr")]
    [InlineData("FR", "fr")]
    [InlineData("fr-CA", "fr")]
    [InlineData("fr_BE", "fr")]
    [InlineData("en-GB", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("klingon", "en")]
    public void Normalize_maps_any_value_to_a_supported_language(string? input, string expected) =>
        Assert.Equal(expected, AppLanguages.Normalize(input));

    [Fact]
    public void First_run_language_follows_the_os_culture()
    {
        Assert.Equal("fr", AppLanguages.FromCulture(CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("en", AppLanguages.FromCulture(CultureInfo.GetCultureInfo("ja-JP")));
        Assert.Equal("en", AppLanguages.FromCulture(null));
    }

    [Theory]
    [InlineData("fr", 0, true)]
    [InlineData("fr", 1, true)]
    [InlineData("fr", 2, false)]
    [InlineData("en", 0, false)]
    [InlineData("en", 1, true)]
    [InlineData("en", 2, false)]
    public void Plural_category_follows_each_language(string language, int count, bool singular) =>
        Assert.Equal(singular, AppLanguages.IsSingular(language, count));

    [Fact]
    public void Every_language_has_a_dictionary_with_the_same_keys_as_english()
    {
        var english = LoadStrings("en");
        foreach (var language in AppLanguages.All)
        {
            var strings = LoadStrings(language);
            Assert.Empty(english.Keys.Except(strings.Keys));
            Assert.Empty(strings.Keys.Except(english.Keys));
            Assert.All(strings, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        }
    }

    [Fact]
    public void Translations_keep_the_english_placeholders()
    {
        var english = LoadStrings("en");
        foreach (var language in AppLanguages.All)
        {
            foreach (var (key, value) in LoadStrings(language))
            {
                Assert.True(
                    Placeholders(english[key]).SetEquals(Placeholders(value)),
                    $"{language}:{key} placeholders differ from English.");
            }
        }
    }

    [Fact]
    public void Plural_keys_always_come_in_pairs()
    {
        var keys = LoadStrings("en").Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in keys.Where(key => key.EndsWith(".One", StringComparison.Ordinal)))
        {
            Assert.Contains(key[..^".One".Length] + ".Other", keys);
        }

        foreach (var key in keys.Where(key => key.EndsWith(".Other", StringComparison.Ordinal)))
        {
            Assert.Contains(key[..^".Other".Length] + ".One", keys);
        }
    }

    [Fact]
    public void Every_text_key_used_by_view_models_exists_in_english()
    {
        var keys = LoadStrings("en").Keys.ToHashSet(StringComparer.Ordinal);
        var constants = typeof(TextKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(constants);
        Assert.All(constants, key => Assert.True(
            keys.Contains(key) || (keys.Contains(key + ".One") && keys.Contains(key + ".Other")),
            $"TextKeys value '{key}' is missing from Strings/en.axaml."));
    }

    [Fact]
    public void Every_string_resource_referenced_by_a_view_exists()
    {
        // String keys are dotted ("Settings.Theme"); palette and Fluent resources are not.
        var keys = LoadStrings("en").Keys.ToHashSet(StringComparer.Ordinal);
        var views = Directory.GetFiles(Path.Combine(DesktopSourceDirectory(), "Views"), "*.axaml", SearchOption.AllDirectories);
        Assert.NotEmpty(views);

        var missing = views
            .SelectMany(path => DynamicResourceKey().Matches(File.ReadAllText(path))
                .Select(match => (File: Path.GetFileName(path), Key: match.Groups[1].Value)))
            .Where(reference => reference.Key.Contains('.', StringComparison.Ordinal) && !keys.Contains(reference.Key))
            .Select(reference => $"{reference.File}: {reference.Key}")
            .Distinct()
            .ToArray();

        Assert.Empty(missing);
    }

    private static Dictionary<string, string> LoadStrings(string language)
    {
        var path = Path.Combine(DesktopSourceDirectory(), "Strings", $"{language}.axaml");
        var document = XDocument.Load(path);
        return document.Root!
            .Elements(Xaml + "String")
            .ToDictionary(element => (string)element.Attribute(Xaml + "Key")!, element => element.Value, StringComparer.Ordinal);
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    internal static string DesktopSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AgenStart.Desktop");
            if (Directory.Exists(Path.Combine(candidate, "Strings")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/AgenStart.Desktop/Strings was not found above the test output folder.");
    }

    [GeneratedRegex(@"\{\d+(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{DynamicResource\s+([^}\s]+)\s*\}")]
    private static partial Regex DynamicResourceKey();
}
