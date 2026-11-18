using System.Text.Json;
using System.Text.Json.Serialization;
using AgenStart.Platform.Windows.SoftwareInventory;
using AgenStart.Platform.Windows.Uninstall;
using AgenStart.SoftwareInventory;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Tests;

public sealed class UninstallRegistryReaderTests
{
    private static readonly JsonSerializerOptions FixtureJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Collector_MapsRegistryFixtureToTheSameRecordsAsBefore()
    {
        var fixture = LoadFixture();
        var collector = new RegistryInstalledSoftwareCollector(new FixtureRegistrySource(fixture.Probes));

        var result = await collector.CollectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            fixture.ExpectedRecords.Select(expected => new InstalledSoftwareRecord(
                InstalledSoftwareSourceKind.Registry,
                SoftwareInventorySourceIds.WindowsRegistry,
                expected.DisplayName,
                expected.Publisher,
                expected.Version,
                expected.Scope)),
            result.Records);
        Assert.Equal(
            [new InventorySourceStatus(SoftwareInventorySourceIds.WindowsRegistry, InventorySourceState.Complete)],
            result.Sources);
    }

    [Fact]
    public async Task Collector_ReportsPartialReadWhenAnyLocationHadErrors()
    {
        var probes = LoadFixture().Probes.ToList();
        probes[1] = probes[1] with { Entries = [], RootUnreadable = true };
        var collector = new RegistryInstalledSoftwareCollector(new FixtureRegistrySource(probes));

        var result = await collector.CollectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [new InventorySourceStatus(
                SoftwareInventorySourceIds.WindowsRegistry,
                InventorySourceState.Partial,
                "registry.partial-read",
                "Some installed-software registry entries could not be read as the current user.")],
            result.Sources);
        Assert.Contains(result.Records, record => record.DisplayName == "zeta Tool");
    }

    [Fact]
    public async Task Collector_IgnoresUnreadableSecondaryValues()
    {
        var location = new UninstallRegistryLocation(ProgramScope.Machine, UninstallRegistryView.Registry64);
        var entry = new UninstallRegistryEntry
        {
            Location = location,
            KeyName = "App",
            KeyPath = @"HKEY_LOCAL_MACHINE\X\App",
            DisplayName = "App",
            HasUnreadableValues = true
        };
        var collector = new RegistryInstalledSoftwareCollector(
            new FixtureRegistrySource([new UninstallRegistryProbeResult(location, [entry])]));

        var result = await collector.CollectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InventorySourceState.Complete, Assert.Single(result.Sources).State);
        Assert.Single(result.Records);
    }

    [Fact]
    public async Task Collector_OnThisMachineMatchesThePreviousImplementationExactly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The live registry exists only on Windows.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await new LegacyRegistryInstalledSoftwareCollector().CollectAsync(cancellationToken);
        var after = await new RegistryInstalledSoftwareCollector().CollectAsync(cancellationToken);

        Assert.Equal(before.Records, after.Records);
        Assert.Equal(before.Sources, after.Sources);
    }

    [Fact]
    public void Reader_OnThisMachineReadsEveryLocationInPrecedenceOrder()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The live registry exists only on Windows.");
        }

        var probes = new UninstallRegistryReader().ReadAll(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new UninstallRegistryLocation(ProgramScope.Machine, UninstallRegistryView.Registry64),
                new UninstallRegistryLocation(ProgramScope.Machine, UninstallRegistryView.Registry32),
                new UninstallRegistryLocation(ProgramScope.User, UninstallRegistryView.Registry64),
                new UninstallRegistryLocation(ProgramScope.User, UninstallRegistryView.Registry32)
            ],
            probes.Select(probe => probe.Location));
        Assert.All(
            probes.SelectMany(probe => probe.Entries.Select(entry => (probe, entry))),
            pair =>
            {
                Assert.Equal(pair.probe.Location, pair.entry.Location);
                Assert.EndsWith(@"\" + pair.entry.KeyName, pair.entry.KeyPath, StringComparison.Ordinal);
            });
    }

    [Theory]
    [InlineData(ProgramScope.Machine, UninstallRegistryView.Registry64, true, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(ProgramScope.Machine, UninstallRegistryView.Registry32, true, @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(ProgramScope.Machine, UninstallRegistryView.Registry32, false, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(ProgramScope.User, UninstallRegistryView.Registry64, true, @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    [InlineData(ProgramScope.User, UninstallRegistryView.Registry32, true, @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")]
    public void Reader_KeyPathIsWhereTheKeyIsPhysicallyStored(
        ProgramScope scope,
        UninstallRegistryView view,
        bool is64BitOperatingSystem,
        string expected)
    {
        Assert.Equal(
            expected,
            UninstallRegistryReader.RootKeyPath(new UninstallRegistryLocation(scope, view), is64BitOperatingSystem));
    }

    [Fact]
    public async Task Inventory_ReadsThroughTheRegistrySourceOffTheCallingThread()
    {
        var inventory = new WindowsInstalledProgramInventory(new FixtureRegistrySource(LoadFixture().Probes));

        var result = await inventory.CaptureAsync(TestContext.Current.CancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(InstalledProgramInventoryStatus.Unavailable, result.Status);
            return;
        }

        Assert.Equal(InstalledProgramInventoryStatus.Partial, result.Status);
        Assert.Equal(
            ["Alpha", "Alpha", "Mid Player", "Runtime (x64)", "zeta Tool"],
            result.Programs.Select(program => program.DisplayName));
        Assert.All(result.Programs, program => Assert.Null(program.WinGetPackageId));
    }

    private static RegistryFixture LoadFixture()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "uninstall-registry-probes.json"));
        var fixture = JsonSerializer.Deserialize<RegistryFixtureDocument>(json, FixtureJson)
            ?? throw new InvalidOperationException("The registry fixture is empty.");

        var probes = fixture.Probes.Select(probe =>
        {
            var location = new UninstallRegistryLocation(probe.Scope, probe.View);
            return new UninstallRegistryProbeResult(
                location,
                probe.Entries.Select(entry => new UninstallRegistryEntry
                {
                    Location = location,
                    KeyName = entry.KeyName,
                    KeyPath = $@"{UninstallRegistryReader.RootKeyPath(location, is64BitOperatingSystem: true)}\{entry.KeyName}",
                    DisplayName = entry.DisplayName,
                    Publisher = entry.Publisher,
                    DisplayVersion = entry.DisplayVersion,
                    UninstallString = entry.UninstallString,
                    SystemComponent = entry.SystemComponent,
                    WindowsInstaller = entry.WindowsInstaller,
                    ParentKeyName = entry.ParentKeyName,
                    ReleaseType = entry.ReleaseType,
                    HasUnreadableValues = entry.HasUnreadableValues
                }).ToArray(),
                UnreadableKeyCount: probe.UnreadableKeyCount);
        }).ToArray();

        return new RegistryFixture(probes, fixture.ExpectedRecords);
    }

    private sealed record RegistryFixture(
        IReadOnlyList<UninstallRegistryProbeResult> Probes,
        IReadOnlyList<ExpectedRecord> ExpectedRecords);

    private sealed record RegistryFixtureDocument(
        IReadOnlyList<FixtureProbe> Probes,
        IReadOnlyList<ExpectedRecord> ExpectedRecords);

    private sealed record FixtureProbe(
        ProgramScope Scope,
        UninstallRegistryView View,
        IReadOnlyList<FixtureEntry> Entries,
        int UnreadableKeyCount = 0);

    private sealed record FixtureEntry(
        string KeyName,
        string? DisplayName = null,
        string? Publisher = null,
        string? DisplayVersion = null,
        string? UninstallString = null,
        bool SystemComponent = false,
        bool WindowsInstaller = false,
        string? ParentKeyName = null,
        string? ReleaseType = null,
        bool HasUnreadableValues = false);

    private sealed record ExpectedRecord(
        string DisplayName,
        string? Publisher,
        string? Version,
        InstalledSoftwareScope Scope);

    private sealed class FixtureRegistrySource(IReadOnlyList<UninstallRegistryProbeResult> probes) : IUninstallRegistrySource
    {
        public IReadOnlyList<UninstallRegistryProbeResult> ReadAll(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return probes;
        }
    }
}
