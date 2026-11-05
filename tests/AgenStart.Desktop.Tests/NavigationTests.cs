using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels;

namespace AgenStart.Desktop.Tests;

public sealed class NavigationTests
{
    private static readonly NavigationState Nothing = default;

    [Theory]
    [InlineData(AppStep.Overview)]
    [InlineData(AppStep.History)]
    [InlineData(AppStep.Settings)]
    public void Home_history_and_settings_are_always_available(AppStep step) =>
        Assert.True(NavigationGate.IsAvailable(step, Nothing));

    [Theory]
    [InlineData(AppStep.YourPc)]
    [InlineData(AppStep.UsageProfile)]
    [InlineData(AppStep.Recommendations)]
    [InlineData(AppStep.Review)]
    [InlineData(AppStep.Installation)]
    [InlineData(AppStep.Report)]
    public void Flow_steps_are_locked_before_their_data_exists(AppStep step) =>
        Assert.False(NavigationGate.IsAvailable(step, Nothing));

    [Theory]
    [InlineData(AppStep.YourPc, true, false, false, false, false)]
    [InlineData(AppStep.UsageProfile, true, false, false, false, false)]
    [InlineData(AppStep.Recommendations, false, true, false, false, false)]
    [InlineData(AppStep.Review, false, false, true, false, false)]
    [InlineData(AppStep.Installation, false, false, false, true, false)]
    [InlineData(AppStep.Installation, false, false, false, false, true)]
    [InlineData(AppStep.Report, false, false, false, false, true)]
    public void Each_flow_step_opens_once_its_own_data_exists(
        AppStep step, bool snapshot, bool recommendations, bool review, bool installation, bool report) =>
        Assert.True(NavigationGate.IsAvailable(step, new NavigationState(snapshot, recommendations, review, installation, report)));

    [Fact]
    public void A_fresh_shell_only_opens_home_history_and_settings()
    {
        using var shell = new MainWindowViewModel(text: new KeyLocalizer());

        Assert.Equal(
            [AppStep.Overview, AppStep.YourPc, AppStep.UsageProfile, AppStep.Recommendations, AppStep.Review, AppStep.Installation, AppStep.Report],
            shell.FlowTabs.Select(tab => tab.Step));
        Assert.Equal([AppStep.History, AppStep.Settings], shell.UtilityTabs.Select(tab => tab.Step));
        Assert.Equal([AppStep.Overview], shell.FlowTabs.Where(tab => tab.IsEnabled).Select(tab => tab.Step));
        Assert.All(shell.UtilityTabs, tab => Assert.True(tab.IsEnabled));
        Assert.Equal([AppStep.Overview], shell.FlowTabs.Concat(shell.UtilityTabs).Where(tab => tab.IsActive).Select(tab => tab.Step));
        Assert.All(shell.FlowTabs.Concat(shell.UtilityTabs), tab => Assert.Equal(tab.IsEnabled, tab.SelectCommand.CanExecute(null)));
    }

    [Fact]
    public void Selecting_a_locked_tab_is_a_no_op()
    {
        using var shell = new MainWindowViewModel(text: new KeyLocalizer());
        var report = shell.FlowTabs.Single(tab => tab.Step == AppStep.Report);

        Assert.False(shell.SelectTab(AppStep.Report));
        report.SelectCommand.Execute(null);

        Assert.Equal(AppStep.Overview, shell.CurrentStep);
    }

    [Fact]
    public void The_current_step_tab_stays_enabled_and_active()
    {
        using var shell = new MainWindowViewModel(text: new KeyLocalizer());
        var installation = shell.FlowTabs.Single(tab => tab.Step == AppStep.Installation);
        var raised = 0;
        installation.SelectCommand.CanExecuteChanged += (_, _) => raised++;

        // The app moves to Installation just before the queue is filled.
        shell.ShowStep(AppStep.Installation);

        Assert.True(installation.IsActive);
        Assert.True(installation.IsEnabled);
        Assert.True(installation.SelectCommand.CanExecute(null));
        Assert.Equal(1, raised);

        Assert.True(shell.SelectTab(AppStep.Settings));
        Assert.False(installation.IsActive);
        Assert.False(installation.IsEnabled);
        Assert.True(shell.SelectTab(AppStep.Overview));
    }

    [Fact]
    public void Tab_labels_follow_the_language()
    {
        var text = new KeyLocalizer();
        using var shell = new MainWindowViewModel(text: text);
        var home = shell.FlowTabs[0];
        var changed = new List<string?>();
        home.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("en:Shell.Overview", home.Label);
        text.Switch("fr");

        Assert.Equal("fr:Shell.Overview", home.Label);
        Assert.Contains(nameof(NavigationTabViewModel.Label), changed);
    }

    [Fact]
    public void Every_tab_label_key_exists_in_both_dictionaries()
    {
        using var shell = new MainWindowViewModel(text: new KeyLocalizer());
        var root = LocalizationTests.DesktopSourceDirectory();
        var english = File.ReadAllText(Path.Combine(root, "Strings", "en.axaml"));
        var french = File.ReadAllText(Path.Combine(root, "Strings", "fr.axaml"));

        Assert.All(shell.FlowTabs.Concat(shell.UtilityTabs), tab =>
        {
            Assert.Contains($"x:Key=\"{tab.LabelKey}\"", english, StringComparison.Ordinal);
            Assert.Contains($"x:Key=\"{tab.LabelKey}\"", french, StringComparison.Ordinal);
        });
    }

    private sealed class KeyLocalizer : LocalizerBase
    {
        private string _language = "en";

        public override string Language => _language;

        public override string this[string key] => $"{_language}:{key}";

        public void Switch(string language)
        {
            _language = language;
            RaiseLanguageChanged();
        }
    }
}
