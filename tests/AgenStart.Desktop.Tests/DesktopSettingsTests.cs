using AgenStart.Desktop.LocalData;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.Tests;

public sealed class DesktopSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agenstart-tests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Legacy_settings_load_as_an_existing_user_who_skips_the_onboarding()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, """{ "analyzeOnStartup": false, "theme": "Dark", "unknownFutureField": 3 }""", TestContext.Current.CancellationToken);

        var settings = await new LocalExperienceStore(_directory).LoadSettingsAsync(TestContext.Current.CancellationToken);

        // Written before the onboarding existed: this person already uses the app.
        Assert.Equal(new DesktopSettings(AnalyzeOnStartup: false, Theme: "Dark", Language: null, OnboardingCompleted: true), settings);
    }

    [Fact]
    public async Task An_onboarding_left_midway_starts_again()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, """{ "theme": "Dark", "language": "fr", "onboardingCompleted": false }""", TestContext.Current.CancellationToken);

        var settings = await new LocalExperienceStore(_directory).LoadSettingsAsync(TestContext.Current.CancellationToken);

        Assert.False(settings.OnboardingCompleted);
        Assert.Equal("fr", settings.Language);
    }

    [Fact]
    public async Task Missing_or_corrupt_settings_load_the_defaults()
    {
        var store = new LocalExperienceStore(_directory);
        Assert.Equal(new DesktopSettings(), await store.LoadSettingsAsync(TestContext.Current.CancellationToken));

        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{ not json", TestContext.Current.CancellationToken);
        Assert.Equal(new DesktopSettings(), await store.LoadSettingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Settings_round_trip_every_field()
    {
        var store = new LocalExperienceStore(_directory);
        var saved = new DesktopSettings(AnalyzeOnStartup: false, Theme: "Dark", Language: "fr", OnboardingCompleted: true);

        await store.SaveSettingsAsync(saved, TestContext.Current.CancellationToken);

        Assert.Equal(saved, await store.LoadSettingsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Completing_onboarding_keeps_other_preferences_and_is_written_once()
    {
        var store = new LocalExperienceStore(_directory);
        var initial = new DesktopSettings(AnalyzeOnStartup: false, Theme: "Dark");
        var viewModel = new LocalExperienceViewModel(store, initialSettings: initial);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(File.Exists(SettingsPath));

        await viewModel.CompleteOnboardingAsync();

        var written = await store.LoadSettingsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(initial with { OnboardingCompleted = true, Language = AppLanguages.Default }, written);
        Assert.True(viewModel.OnboardingCompleted);

        // A converged second apply writes nothing.
        File.Delete(SettingsPath);
        await viewModel.CompleteOnboardingAsync();
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Data_directory_can_be_relocated_by_environment_variable()
    {
        var previous = Environment.GetEnvironmentVariable(LocalExperienceStore.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(LocalExperienceStore.DataDirectoryVariable, _directory);
            Assert.Equal(Path.GetFullPath(_directory), LocalExperienceStore.DefaultDirectory());

            Environment.SetEnvironmentVariable(LocalExperienceStore.DataDirectoryVariable, null);
            Assert.EndsWith("AgenStart", LocalExperienceStore.DefaultDirectory(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalExperienceStore.DataDirectoryVariable, previous);
        }
    }
}
