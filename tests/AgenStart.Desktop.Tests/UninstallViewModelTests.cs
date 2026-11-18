using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels;
using AgenStart.Desktop.ViewModels.Steps;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.Tests;

public sealed class UninstallViewModelTests
{
    private static readonly InstalledProgram[] Sample =
    [
        Program("b", "Beta Studio", "Contoso", size: 300L * 1024 * 1024, date: new DateOnly(2024, 5, 1)),
        Program("a", "Alpha Éditeur", "Fabrikam", size: 2L * 1024 * 1024 * 1024, date: null),
        Program("c", "charlie Tool", null, size: null, date: new DateOnly(2025, 1, 15))
    ];

    [Fact]
    public async Task Programs_are_read_once_in_the_background_and_sorted_by_name()
    {
        var inventory = new FakeInventory(Complete(Sample));
        var page = Create(inventory);

        Assert.Empty(page.Programs);
        Assert.Equal(string.Empty, page.HeaderText);

        page.EnsureLoaded();
        await page.EnsureLoadedAsync();

        Assert.Equal(1, inventory.Calls);
        Assert.False(page.IsLoading);
        Assert.Equal(["Alpha Éditeur", "Beta Studio", "charlie Tool"], page.Programs.Select(row => row.DisplayName));
        Assert.Equal(3, page.ProgramCount);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2L * 1024 * 1024 * 1024 + 300L * 1024 * 1024, page.TotalSizeBytes);
        Assert.Equal("3 programs · 2.3 GB", page.HeaderText);
        Assert.False(page.HasNotice);
        Assert.True(page.ShowList);
    }

    [Fact]
    public async Task Refresh_reads_the_inventory_again()
    {
        var inventory = new FakeInventory(Complete(Sample));
        var page = Create(inventory);
        await page.EnsureLoadedAsync();

        page.RefreshCommand.Execute(null);
        await page.RefreshAsync();

        Assert.True(inventory.Calls >= 2);
        Assert.Equal(3, page.ProgramCount);
    }

    [Theory]
    [InlineData("editeur", new[] { "Alpha Éditeur" })]
    [InlineData("  CONTOSO ", new[] { "Beta Studio" })]
    [InlineData("t", new[] { "Alpha Éditeur", "Beta Studio", "charlie Tool" })]
    [InlineData("nothing like it", new string[0])]
    public async Task Search_matches_name_or_publisher_ignoring_case_and_accents(string query, string[] expected)
    {
        var page = Create(new FakeInventory(Complete(Sample)));
        await page.EnsureLoadedAsync();

        page.SearchText = query;

        Assert.Equal(expected, page.Programs.Select(row => row.DisplayName));
        Assert.Equal(expected.Length, page.ProgramCount);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(expected.Length == 0, page.ShowNoMatch);
        Assert.Equal(expected.Length > 0, page.ShowList);
    }

    [Fact]
    public async Task Search_keeps_the_selection_while_it_stays_visible()
    {
        var page = Create(new FakeInventory(Complete(Sample)));
        await page.EnsureLoadedAsync();
        page.SelectedProgram = page.Programs.Single(row => row.DisplayName == "Beta Studio");

        page.SearchText = "beta";
        Assert.Equal("Beta Studio", page.SelectedProgram?.DisplayName);
        Assert.True(page.HasSelection);

        page.SearchText = "alpha";
        Assert.Null(page.SelectedProgram);
        Assert.False(page.HasSelection);
        Assert.Equal("1 program · 2 GB", page.HeaderText);

        page.SearchText = "zzz";
        Assert.True(page.ShowNoMatch);
        Assert.Equal("en:Uninstall.NoMatch", page.NoMatchText);
    }

    [Theory]
    [InlineData(UninstallSortOrder.Name, new[] { "Alpha Éditeur", "Beta Studio", "charlie Tool" })]
    [InlineData(UninstallSortOrder.Publisher, new[] { "Beta Studio", "Alpha Éditeur", "charlie Tool" })]
    [InlineData(UninstallSortOrder.Size, new[] { "Alpha Éditeur", "Beta Studio", "charlie Tool" })]
    [InlineData(UninstallSortOrder.Date, new[] { "charlie Tool", "Beta Studio", "Alpha Éditeur" })]
    public async Task Sort_orders_known_values_first(UninstallSortOrder order, string[] expected)
    {
        var page = Create(new FakeInventory(Complete(Sample)));
        await page.EnsureLoadedAsync();

        page.SortOrder = order;

        Assert.Equal(expected, page.Programs.Select(row => row.DisplayName));
        Assert.Equal(order == UninstallSortOrder.Name, page.IsSortByName);
        Assert.Equal(order == UninstallSortOrder.Publisher, page.IsSortByPublisher);
        Assert.Equal(order == UninstallSortOrder.Size, page.IsSortBySize);
        Assert.Equal(order == UninstallSortOrder.Date, page.IsSortByDate);
    }

    [Fact]
    public async Task Sort_commands_switch_the_order()
    {
        var page = Create(new FakeInventory(Complete(Sample)));
        await page.EnsureLoadedAsync();

        page.SortByDateCommand.Execute(null);
        Assert.Equal(UninstallSortOrder.Date, page.SortOrder);
        page.SortBySizeCommand.Execute(null);
        Assert.Equal(UninstallSortOrder.Size, page.SortOrder);
        page.SortByPublisherCommand.Execute(null);
        Assert.Equal(UninstallSortOrder.Publisher, page.SortOrder);
        page.SortByNameCommand.Execute(null);
        Assert.Equal(UninstallSortOrder.Name, page.SortOrder);
    }

    [Fact]
    public async Task A_partial_read_lists_what_was_read_with_a_notice()
    {
        var page = Create(new FakeInventory(new InstalledProgramInventoryResult(
            Sample,
            InstalledProgramInventoryStatus.Partial,
            [new InventoryDiagnostic(RegistryInstalledProgramInventory.PartialReadCode, "HKEY_LOCAL_MACHINE")])));

        await page.EnsureLoadedAsync();

        Assert.Equal(3, page.ProgramCount);
        Assert.True(page.HasNotice);
        Assert.Equal("en:Uninstall.Notice.Partial", page.Notice);
    }

    [Fact]
    public async Task An_unavailable_inventory_shows_a_message()
    {
        var page = Create(new FakeInventory(new InstalledProgramInventoryResult(
            [],
            InstalledProgramInventoryStatus.Unavailable,
            [new InventoryDiagnostic("registry.unsupported-platform", "Unavailable.")])));

        await page.EnsureLoadedAsync();

        Assert.Equal("en:Uninstall.Notice.Unavailable", page.Notice);
        Assert.True(page.ShowNoMatch);
        Assert.Equal("en:Uninstall.NoPrograms", page.NoMatchText);
    }

    [Fact]
    public async Task A_failing_inventory_shows_a_message_instead_of_crashing()
    {
        var page = Create(new FakeInventory(failure: new InvalidOperationException("boom")));

        await page.EnsureLoadedAsync();

        Assert.False(page.IsLoading);
        Assert.Empty(page.Programs);
        Assert.True(page.HasNotice);
        Assert.Equal("en:Uninstall.Notice.Failed", page.Notice);
        Assert.True(page.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Row_texts_are_formatted_by_the_view_model_and_follow_the_language()
    {
        var text = new TestLocalizer();
        var page = Create(new FakeInventory(Complete(
        [
            Program("m", "Msi App", "Contoso", size: 1536, date: null) with
            {
                Scope = ProgramScope.User,
                InstallerKind = InstallerKind.Msi,
                MsiProductCode = "{11111111-2222-3333-4444-555555555555}",
                InstallLocation = @"C:\Apps\Msi"
            }
        ])), text);
        await page.EnsureLoadedAsync();
        var row = Assert.Single(page.Programs);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Contoso · 1.0", row.Caption);
        Assert.Equal("1.5 KB", row.SizeText);
        Assert.Equal("en:Common.Unknown", row.InstallDateText);
        Assert.Equal("en:Uninstall.Scope.User", row.ScopeText);
        Assert.Equal("en:Uninstall.Source.Registry", row.SourceText);
        Assert.Equal("en:Uninstall.Installer.Msi", row.InstallerKindText);
        Assert.Equal("en:Uninstall.Silent.Likely", row.SilentUninstallText);
        Assert.Equal(@"C:\Apps\Msi", row.InstallLocation);

        text.Switch("fr");

        Assert.Equal("fr:Uninstall.Scope.User", row.ScopeText);
        Assert.Equal("1 programme · 1,5 Ko", page.HeaderText);
        Assert.Contains(string.Empty, changed);
    }

    [Fact]
    public void The_page_only_receives_read_ports()
    {
        var allowed = new[] { typeof(IInstalledProgramInventory), typeof(IUiDispatcher), typeof(ILocalizer) };

        Assert.All(
            typeof(UninstallViewModel).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => Assert.Contains(parameter.ParameterType, allowed));
    }

    [Fact]
    public async Task Opening_the_uninstall_tab_starts_the_read_without_blocking()
    {
        var inventory = new FakeInventory(Complete(Sample));
        using var shell = new MainWindowViewModel(
            dispatcher: new ImmediateDispatcher(),
            text: new TestLocalizer(),
            programInventory: inventory);

        Assert.Equal(0, inventory.Calls);
        Assert.True(shell.SelectTab(AppStep.Uninstall));
        Assert.True(shell.IsUninstall);
        await shell.Uninstall.EnsureLoadedAsync();

        Assert.Equal(1, inventory.Calls);
        Assert.Equal(3, shell.Uninstall.ProgramCount);

        Assert.True(shell.SelectTab(AppStep.Overview));
        Assert.True(shell.SelectTab(AppStep.Uninstall));
        Assert.Equal(1, inventory.Calls);
    }

    private static UninstallViewModel Create(FakeInventory inventory, ILocalizer? text = null) =>
        new(inventory, new ImmediateDispatcher(), text ?? new TestLocalizer());

    private static InstalledProgramInventoryResult Complete(IReadOnlyList<InstalledProgram> programs) =>
        new(programs, InstalledProgramInventoryStatus.Complete, []);

    private static InstalledProgram Program(string id, string name, string? publisher, long? size, DateOnly? date) => new()
    {
        Id = id,
        DisplayName = name,
        Publisher = publisher,
        Version = "1.0",
        Scope = ProgramScope.Machine,
        Source = ProgramSource.Registry,
        EstimatedSizeBytes = size,
        InstallDate = date
    };

    private sealed class FakeInventory(
        InstalledProgramInventoryResult? result = null,
        Exception? failure = null) : IInstalledProgramInventory
    {
        public int Calls { get; private set; }

        public async Task<InstalledProgramInventoryResult> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            if (failure is not null)
            {
                throw failure;
            }

            return result ?? new InstalledProgramInventoryResult([], InstalledProgramInventoryStatus.Complete, []);
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    /// <summary>Real templates for the summary and units; every other key comes back as language:key.</summary>
    private sealed class TestLocalizer : LocalizerBase
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Templates = new(StringComparer.Ordinal)
        {
            ["en"] = new(StringComparer.Ordinal)
            {
                ["Uninstall.Summary.One"] = "{0} program · {1}",
                ["Uninstall.Summary.Other"] = "{0} programs · {1}",
                ["Units.Gigabytes"] = "{0:0.#} GB",
                ["Units.Megabytes"] = "{0:0.0} MB",
                ["Units.Kilobytes"] = "{0:0.0} KB",
                ["Units.Bytes"] = "{0} B"
            },
            ["fr"] = new(StringComparer.Ordinal)
            {
                ["Uninstall.Summary.One"] = "{0} programme · {1}",
                ["Uninstall.Summary.Other"] = "{0} programmes · {1}",
                ["Units.Gigabytes"] = "{0:0.#} Go",
                ["Units.Megabytes"] = "{0:0.0} Mo",
                ["Units.Kilobytes"] = "{0:0.0} Ko",
                ["Units.Bytes"] = "{0} o"
            }
        };

        private string _language = "en";

        public override string Language => _language;

        public override string this[string key] =>
            Templates[_language].TryGetValue(key, out var template) ? template : $"{_language}:{key}";

        public void Switch(string language)
        {
            _language = language;
            RaiseLanguageChanged();
        }
    }
}
