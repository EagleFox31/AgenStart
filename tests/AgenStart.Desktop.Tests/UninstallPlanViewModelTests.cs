using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgenStart.Desktop.Services;
using AgenStart.Desktop.ViewModels;
using AgenStart.Desktop.ViewModels.Steps;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.Tests;

public sealed partial class UninstallViewModelTests
{
    [Fact]
    public async Task A_prepared_plan_shows_three_sections_with_the_default_selection()
    {
        var page = await PlannedPage(new FakePlanSource(_ => SamplePlan("b")));
        var plan = Assert.IsType<UninstallPlanViewModel>(page.CurrentPlan);

        Assert.True(page.HasPlan);
        Assert.Equal(["high", "medium"], plan.Leftovers.Select(item => item.Item.ItemId));
        Assert.Equal([true, false], plan.Leftovers.Select(item => item.IsSelected));
        Assert.Equal("protected", Assert.Single(plan.ProtectedItems).Item.ItemId);
        Assert.False(plan.ProtectedItems[0].IsSelected);
        Assert.False(plan.HasProtectedSelected);
        Assert.Equal(1, plan.LowConfidenceCount);
        Assert.True(plan.HasRunning);
        Assert.True(plan.HasSkipped);
        Assert.True(plan.HasCommandPreview);
        Assert.Equal("en:Uninstall.Plan.Strategy.Msi", plan.StrategyText);
        Assert.Equal(["high"], plan.SelectedItemIds);
        Assert.Equal("en:Uninstall.Plan.Kind.Folder", plan.Leftovers[0].KindText);
        Assert.Equal("en:Uninstall.Plan.Confidence.High", plan.Leftovers[0].ConfidenceText);
        Assert.Equal("en:Uninstall.Plan.Class.UserData", plan.ProtectedItems[0].ClassText);
        Assert.Equal("1.0 MB", plan.Leftovers[0].SizeText);
        Assert.Equal("en:Common.Unknown", plan.Leftovers[1].SizeText);
        Assert.False(page.HasPlanNotice);
    }

    [Fact]
    public async Task Checking_a_protected_item_warns_and_only_changes_the_in_memory_selection()
    {
        var source = new FakePlanSource(_ => SamplePlan("b"));
        var target = new FakeExportTarget();
        var page = await PlannedPage(source, target);
        var plan = page.CurrentPlan!;
        var original = plan.Plan;
        var changes = new List<string?>();
        plan.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        plan.ProtectedItems[0].IsSelected = true;
        plan.Leftovers[0].IsSelected = false;

        Assert.True(plan.HasProtectedSelected);
        Assert.Contains(nameof(UninstallPlanViewModel.HasProtectedSelected), changes);
        Assert.Equal(["protected"], plan.SelectedItemIds);
        Assert.Same(original, plan.Plan);
        Assert.True(original.Leftovers[0].DefaultSelected);
        Assert.False(original.ProtectedUserData[0].DefaultSelected);
        Assert.Equal(1, source.Calls);
        Assert.Empty(target.Saved);
    }

    [Fact]
    public async Task Exporting_writes_the_current_selection_as_a_plan_that_was_not_executed()
    {
        var target = new FakeExportTarget();
        var page = await PlannedPage(new FakePlanSource(_ => SamplePlan("b")), target);
        page.CurrentPlan!.Leftovers[1].IsSelected = true;

        Assert.True(page.ExportPlanCommand.CanExecute(null));
        await page.ExportPlanAsync();

        var (name, content) = Assert.Single(target.Saved);
        Assert.Equal("agenstart-uninstall-plan-beta-studio.json", name);
        using var json = JsonDocument.Parse(content);
        Assert.False(json.RootElement.GetProperty("executed").GetBoolean());
        Assert.Equal("b", json.RootElement.GetProperty("programId").GetString());
        var selected = json.RootElement.GetProperty("leftovers").EnumerateArray()
            .Where(item => item.GetProperty("selected").GetBoolean())
            .Select(item => item.GetProperty("itemId").GetString());
        Assert.Equal(["high", "medium"], selected);
        Assert.Equal("en:Uninstall.Plan.Exported", page.PlanNotice);
        Assert.False(page.IsExporting);
    }

    [Fact]
    public async Task A_cancelled_export_says_nothing_and_a_failed_one_says_so()
    {
        var cancelled = new FakeExportTarget { Result = false };
        var page = await PlannedPage(new FakePlanSource(_ => SamplePlan("b")), cancelled);
        await page.ExportPlanAsync();
        Assert.False(page.HasPlanNotice);

        var failing = new FakeExportTarget { Failure = new IOException("disk full") };
        page = await PlannedPage(new FakePlanSource(_ => SamplePlan("b")), failing);
        await page.ExportPlanAsync();
        Assert.Equal("en:Uninstall.Plan.ExportFailed", page.PlanNotice);
    }

    [Fact]
    public async Task Planning_needs_a_selection_and_a_source_and_export_needs_a_target()
    {
        var withoutSource = Create(new FakeInventory(Complete(Sample)));
        await withoutSource.EnsureLoadedAsync();
        withoutSource.SelectedProgram = withoutSource.Programs[0];
        Assert.False(withoutSource.PreparePlanCommand.CanExecute(null));

        var source = new FakePlanSource(_ => SamplePlan("b"));
        var page = CreateWithPlan(source, exportTarget: null);
        await page.EnsureLoadedAsync();
        Assert.False(page.PreparePlanCommand.CanExecute(null));

        page.SelectedProgram = page.Programs.Single(row => row.Program.Id == "b");
        Assert.True(page.PreparePlanCommand.CanExecute(null));
        await page.PreparePlanAsync();
        Assert.True(page.HasPlan);
        Assert.False(page.ExportPlanCommand.CanExecute(null));
    }

    [Fact]
    public async Task Changing_the_selection_clears_the_plan_and_drops_a_late_result()
    {
        var gate = new TaskCompletionSource<UninstallPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakePlanSource(_ => gate.Task);
        var page = CreateWithPlan(source, new FakeExportTarget());
        await page.EnsureLoadedAsync();
        page.SelectedProgram = page.Programs.Single(row => row.Program.Id == "b");

        var planning = page.PreparePlanAsync();
        Assert.True(page.IsPlanning);
        page.SelectedProgram = page.Programs.Single(row => row.Program.Id == "a");
        Assert.False(page.IsPlanning);
        Assert.True(source.LastToken.IsCancellationRequested);

        gate.SetResult(SamplePlan("b"));
        await planning;

        Assert.Null(page.CurrentPlan);
        Assert.False(page.HasPlanNotice);
    }

    [Fact]
    public async Task A_failed_plan_or_invalid_rules_are_reported()
    {
        var failed = await PlannedPage(new FakePlanSource(_ => Task.FromException<UninstallPlan>(new InvalidOperationException("boom"))));
        Assert.Null(failed.CurrentPlan);
        Assert.Equal("en:Uninstall.Plan.Failed", failed.PlanNotice);

        var invalid = await PlannedPage(new FakePlanSource(_ => SamplePlan("b") with
        {
            Leftovers = [],
            ProtectedUserData = [],
            Diagnostics = [new PlanDiagnostic(UninstallPlanService.RulesInvalid, "bad rules")]
        }));
        Assert.True(invalid.CurrentPlan!.RulesInvalid);
        Assert.False(invalid.CurrentPlan.HasLeftovers);
        Assert.Equal("en:Uninstall.Plan.RulesInvalid", invalid.PlanNotice);
    }

    [Fact]
    public async Task The_plan_texts_follow_the_language()
    {
        var text = new TestLocalizer();
        var page = CreateWithPlan(new FakePlanSource(_ => SamplePlan("b")), null, text);
        await page.EnsureLoadedAsync();
        page.SelectedProgram = page.Programs.Single(row => row.Program.Id == "b");
        await page.PreparePlanAsync();
        var refreshed = new List<string?>();
        page.CurrentPlan!.Leftovers[0].PropertyChanged += (_, e) => refreshed.Add(e.PropertyName);

        text.Switch("fr");

        Assert.Equal("fr:Uninstall.Plan.Strategy.Msi", page.CurrentPlan.StrategyText);
        Assert.Equal("1,0 Mo", page.CurrentPlan.Leftovers[0].SizeText);
        Assert.Contains(string.Empty, refreshed);
    }

    [Theory]
    [InlineData("Beta Studio", "agenstart-uninstall-plan-beta-studio.json")]
    [InlineData("  ..\\..\\Évil: <name>?  ", "agenstart-uninstall-plan-vil-name.json")]
    [InlineData("", "agenstart-uninstall-plan-program.json")]
    [InlineData(null, "agenstart-uninstall-plan-program.json")]
    public void The_export_file_name_only_keeps_safe_characters(string? programName, string expected) =>
        Assert.Equal(expected, UninstallViewModel.ExportFileName(programName));

    [Fact]
    public void The_default_composition_reaches_no_write_execute_or_kill_capability()
    {
        using var shell = new MainWindowViewModel(dispatcher: new ImmediateDispatcher(), text: new TestLocalizer());

        // Start from the machine-facing ports the page holds (the localizer and dispatcher are shared
        // UI services whose events lead back to the whole shell, so they are not machine ports).
        var ports = typeof(UninstallViewModel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(IInstalledProgramInventory)
                || field.FieldType == typeof(IUninstallPlanSource)
                || field.FieldType == typeof(IUninstallPlanExportTarget))
            .Select(field => field.GetValue(shell.Uninstall))
            .OfType<object>()
            .ToArray();
        Assert.Equal(2, ports.Length);
        var reachable = ports.SelectMany(Reachable).ToHashSet();

        // Every Windows adapter the page can reach is one of the read-only ones.
        var platform = reachable.Where(type => type.Assembly.GetName().Name == "AgenStart.Platform.Windows").ToHashSet();
        Assert.Contains(typeof(AgenStart.Platform.Windows.Uninstall.WindowsRegistryProbe), platform);
        Assert.Subset(ReadOnlyPlatformTypes, platform);

        // No reachable AgenStart type declares a method that sounds like an action on the PC.
        var action = new Regex("^(Delete|Remove|Recycle|Kill|Terminate|Signal|Execute|Run|Launch|Start|Uninstall|Write|Move|Copy|Elevate)", RegexOptions.CultureInvariant);
        var offending = reachable
            .Where(type => type.Assembly.GetName().Name is "AgenStart.Uninstall" or "AgenStart.Platform.Windows")
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => action.IsMatch(method.Name))
                .Select(method => $"{type.Name}.{method.Name}"))
            .ToArray();
        Assert.Empty(offending);
    }

    private static readonly HashSet<Type> ReadOnlyPlatformTypes =
    [
        typeof(AgenStart.Platform.Windows.Uninstall.WindowsInstalledProgramInventory),
        typeof(AgenStart.Platform.Windows.Uninstall.UninstallRegistryReader),
        typeof(AgenStart.Platform.Windows.Uninstall.WindowsKnownFolderResolver),
        typeof(AgenStart.Platform.Windows.Uninstall.WindowsFileSystemProbe),
        typeof(AgenStart.Platform.Windows.Uninstall.WindowsRegistryProbe),
        typeof(AgenStart.Platform.Windows.Uninstall.WindowsShortcutResolver),
        typeof(AgenStart.Platform.Windows.Uninstall.WindowsProcessInspector)
    ];

    /// <summary>Types of every object (and delegate target method) reachable through fields.</summary>
    private static HashSet<Type> Reachable(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var types = new HashSet<Type>();
        var pending = new Stack<object>([root]);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current))
            {
                continue;
            }

            var type = current.GetType();
            types.Add(type);
            if (current is Delegate callback)
            {
                foreach (var single in callback.GetInvocationList())
                {
                    if (single.Method.DeclaringType is { } declaring)
                    {
                        types.Add(declaring);
                    }

                    if (single.Target is { } target)
                    {
                        pending.Push(target);
                    }
                }

                continue;
            }

            if (current is IEnumerable items and not string)
            {
                foreach (var item in items)
                {
                    if (item is not null && !item.GetType().IsPrimitive)
                    {
                        pending.Push(item);
                    }
                }
            }

            for (var level = type; level is not null && level != typeof(object); level = level.BaseType)
            {
                if (!Explorable(level))
                {
                    break;
                }

                foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string) && field.GetValue(current) is { } value)
                    {
                        pending.Push(value);
                    }
                }
            }
        }

        return types;
    }

    private static bool Explorable(Type type) =>
        type.Assembly.GetName().Name!.StartsWith("AgenStart", StringComparison.Ordinal)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Lazy<>));

    private static async Task<UninstallViewModel> PlannedPage(FakePlanSource source, FakeExportTarget? target = null)
    {
        var page = CreateWithPlan(source, target ?? new FakeExportTarget());
        await page.EnsureLoadedAsync();
        page.SelectedProgram = page.Programs.Single(row => row.Program.Id == "b");
        await page.PreparePlanAsync();
        return page;
    }

    private static UninstallViewModel CreateWithPlan(FakePlanSource source, FakeExportTarget? exportTarget, ILocalizer? text = null) =>
        new(new FakeInventory(Complete(Sample)), new ImmediateDispatcher(), text ?? new TestLocalizer(), source, exportTarget);

    private static UninstallPlan SamplePlan(string programId) => new(
        PlanId: "plan-1",
        ProgramId: programId,
        ProgramName: "Beta Studio",
        StateFingerprint: "state",
        RulesVersion: "1",
        Official: new OfficialUninstallStep(OfficialStrategy.Msi, "msiexec.exe", ["/x", "{GUID}"], "msiexec.exe /x {GUID}", false, true),
        Leftovers:
        [
            Item("high", LeftoverKind.Directory, DataClass.AppData, ConfidenceLevel.High, 1024 * 1024, selected: true),
            Item("medium", LeftoverKind.RegistryKey, DataClass.Configuration, ConfidenceLevel.Medium, null, selected: false),
            Item("low", LeftoverKind.File, DataClass.Cache, ConfidenceLevel.Low, 10, selected: false)
        ],
        ProtectedUserData: [Item("protected", LeftoverKind.Directory, DataClass.UserData, ConfidenceLevel.High, 2048, selected: false)],
        Skipped: [new SkippedCandidate("rule", LeftoverKind.Directory, @"C:\Windows", SkipReasons.Forbidden)],
        Processes: [new RunningProcess(42, "beta", @"C:\Program Files\Beta\beta.exe")],
        Thresholds: VolumeThresholds.Default,
        Diagnostics: []);

    private static PlanItem Item(string id, LeftoverKind kind, DataClass dataClass, ConfidenceLevel level, long? size, bool selected) => new(
        id, "rule", kind, $@"C:\Users\me\AppData\Local\Beta\{id}", size, size is null ? null : 1, false, "fp",
        new ConfidenceScore(level switch { ConfidenceLevel.High => 9, ConfidenceLevel.Medium => 5, _ => 1 }, level, []),
        dataClass, selected, false);

    private sealed class FakePlanSource(Func<InstalledProgram, Task<UninstallPlan>> create) : IUninstallPlanSource
    {
        public FakePlanSource(Func<InstalledProgram, UninstallPlan> create)
            : this(program => Task.FromResult(create(program)))
        {
        }

        public int Calls { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public Task<UninstallPlan> CreatePlanAsync(InstalledProgram program, IReadOnlyList<InstalledProgram> allPrograms, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            return create(program);
        }
    }

    private sealed class FakeExportTarget : IUninstallPlanExportTarget
    {
        public bool Result { get; init; } = true;

        public Exception? Failure { get; init; }

        public List<(string Name, string Content)> Saved { get; } = [];

        public Task<bool> SaveAsync(string suggestedFileName, string content, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException<bool>(Failure);
            }

            if (Result)
            {
                Saved.Add((suggestedFileName, content));
            }

            return Task.FromResult(Result);
        }
    }
}
