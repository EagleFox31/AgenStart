using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.SoftwareInventory;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.SoftwareInventory;

namespace AgenStart.Platform.Windows.Tests;

public sealed class InstalledSoftwareInventoryTests
{
    [Fact]
    public void WinGetExportParser_UsesOnlyExpectedSourceAndCapturesVersions()
    {
        var json = File.ReadAllText(FixturePath("winget-installed-export.json"));

        var records = WinGetInstalledSoftwareExportParser.Parse(json, "winget");

        Assert.Equal(3, records.Count);

        var git = Assert.Single(records, record => record.PackageId == "Git.Git");
        Assert.Equal("2.51.0", git.Version);
        Assert.Equal(PackageProviderIds.WinGet, git.ProviderId);
        Assert.Equal("winget", git.PackageSource);

        var vscode = Assert.Single(records, record => record.PackageId == "Microsoft.VisualStudioCode");
        Assert.Equal("1.103.2", vscode.Version);
        Assert.Equal(InstalledSoftwareScope.User, vscode.Scope);

        var vlc = Assert.Single(records, record => record.PackageId == "VideoLAN.VLC");
        Assert.Null(vlc.Version);
    }

    [Fact]
    public void WinGetExportParser_CanReadMicrosoftStoreSeparately()
    {
        var json = File.ReadAllText(FixturePath("winget-installed-export.json"));

        var records = WinGetInstalledSoftwareExportParser.Parse(json, "msstore");

        var storePackage = Assert.Single(records);
        Assert.Equal("9NKSQGP7F2NH", storePackage.PackageId);
        Assert.Equal("msstore", storePackage.PackageSource);
    }

    [Fact]
    public void WinGetExportCommand_IsExactToTrustedSourceAndOwnedOutput()
    {
        var output = Path.Combine(Path.GetTempPath(), "AgenStart", "inventory-test.json");

        var command = WinGetCommandBuilder.BuildExportInstalled("winget", output);

        Assert.Equal(
            [
                "export",
                "--output", output,
                "--source", "winget",
                "--include-versions",
                "--disable-interactivity"
            ],
            command.Arguments);

        Assert.Throws<ArgumentException>(() =>
            WinGetCommandBuilder.BuildExportInstalled("custom-source", output));
    }

    [Fact]
    public void Resolver_ConfirmsExactProviderIdentityAndVersion()
    {
        var target = Target(
            "visual-studio-code",
            "Visual Studio Code",
            "Microsoft",
            "Microsoft.VisualStudioCode");
        var record = ProviderRecord(
            "Microsoft.VisualStudioCode",
            "1.103.2");
        var snapshot = Snapshot(
            [record, ProviderRecord("Unknown.Publisher.App", "9.0")],
            [CompleteProviderSource("winget")]);

        var result = new SoftwareStateResolver().Resolve([target], snapshot);

        var state = Assert.Single(result.Applications);
        Assert.Equal(SoftwarePresenceState.Installed, state.State);
        Assert.Equal("1.103.2", state.InstalledVersion);
        Assert.Same(record, Assert.Single(state.Evidence));
        Assert.Equal(1, result.UnmappedRecordCount);
    }

    [Fact]
    public void Resolver_UsesExactRegistryNameAndPublisherAsIndependentEvidence()
    {
        var target = new SoftwareDetectionTarget(
            "vlc",
            "VLC media player",
            "VideoLAN",
            [],
            ["VLC media player"]);
        var record = new InstalledSoftwareRecord(
            InstalledSoftwareSourceKind.Registry,
            SoftwareInventorySourceIds.WindowsRegistry,
            "VLC media player",
            "VideoLAN",
            "3.0.21",
            InstalledSoftwareScope.Machine);

        var result = new SoftwareStateResolver().Resolve(
            [target],
            Snapshot([record], [CompleteRegistry()]));

        var state = Assert.Single(result.Applications);
        Assert.Equal(SoftwarePresenceState.Installed, state.State);
        Assert.Equal("3.0.21", state.InstalledVersion);
        Assert.Empty(state.Diagnostics);
    }

    [Fact]
    public void Resolver_DoesNotSilentlyConfirmAmbiguousRegistryMatch()
    {
        var first = new SoftwareDetectionTarget(
            "example-one",
            "Example App",
            "Example Publisher",
            [],
            ["Example App"]);
        var second = new SoftwareDetectionTarget(
            "example-two",
            "Example App",
            "Example Publisher",
            [],
            ["Example App"]);
        var record = new InstalledSoftwareRecord(
            InstalledSoftwareSourceKind.Registry,
            SoftwareInventorySourceIds.WindowsRegistry,
            "Example App",
            "Example Publisher",
            "1.0");

        var result = new SoftwareStateResolver().Resolve(
            [first, second],
            Snapshot([record], [CompleteRegistry()]));

        Assert.All(result.Applications, application =>
        {
            Assert.Equal(SoftwarePresenceState.Unknown, application.State);
            Assert.Contains(
                application.Diagnostics,
                diagnostic => diagnostic.Code == "inventory.ambiguous-registry-match");
        });
        Assert.Equal(1, result.UnmappedRecordCount);
    }

    [Fact]
    public void Resolver_UsesSourceCompletenessToDistinguishMissingFromUnknown()
    {
        var target = Target("git", "Git", "Git Project", "Git.Git");
        var resolver = new SoftwareStateResolver();

        var missing = resolver.Resolve(
            [target],
            Snapshot([], [CompleteProviderSource("winget")]));
        var unknown = resolver.Resolve(
            [target],
            Snapshot(
                [],
                [new InventorySourceStatus(
                    SoftwareInventorySourceIds.ForPackageProvider(PackageProviderIds.WinGet, "winget"),
                    InventorySourceState.Partial,
                    "fixture.partial")]));

        Assert.Equal(SoftwarePresenceState.Missing, Assert.Single(missing.Applications).State);
        Assert.Equal(SoftwarePresenceState.Unknown, Assert.Single(unknown.Applications).State);
    }

    [Fact]
    public void Resolver_KeepsInstalledStateWhenDifferentVersionsAreObserved()
    {
        var target = Target("git", "Git", "Git Project", "Git.Git");
        var snapshot = Snapshot(
            [
                ProviderRecord("Git.Git", "2.50.0"),
                ProviderRecord("Git.Git", "2.51.0")
            ],
            [CompleteProviderSource("winget")]);

        var state = Assert.Single(new SoftwareStateResolver().Resolve([target], snapshot).Applications);

        Assert.Equal(SoftwarePresenceState.Installed, state.State);
        Assert.Null(state.InstalledVersion);
        Assert.Contains(
            state.Diagnostics,
            diagnostic => diagnostic.Code == "inventory.multiple-installed-versions");
    }

    [Fact]
    public async Task CompositeProvider_MergesCollectorsDeduplicatesRecordsAndPreservesStatuses()
    {
        var capturedAt = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var registryRecord = new InstalledSoftwareRecord(
            InstalledSoftwareSourceKind.Registry,
            SoftwareInventorySourceIds.WindowsRegistry,
            "7-Zip",
            "Igor Pavlov",
            "25.01");
        var wingetRecord = ProviderRecord("7zip.7zip", "25.01");

        var provider = new CompositeInstalledSoftwareInventoryProvider(
        [
            new FakeCollector(new InstalledSoftwareCollectionResult(
                [registryRecord],
                [CompleteRegistry()])),
            new FakeCollector(new InstalledSoftwareCollectionResult(
                [registryRecord, wingetRecord],
                [CompleteProviderSource("winget")]))
        ],
        new FixedTimeProvider(capturedAt));

        var snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Records.Count);
        Assert.Equal(2, snapshot.Sources.Count);
        Assert.Equal(capturedAt, snapshot.CapturedAtUtc);
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static SoftwareDetectionTarget Target(
        string id,
        string displayName,
        string publisher,
        string packageId) =>
        new(
            id,
            displayName,
            publisher,
            [new ProviderPackageReference(PackageProviderIds.WinGet, packageId, "winget")],
            [displayName]);

    private static InstalledSoftwareRecord ProviderRecord(
        string packageId,
        string? version) =>
        new(
            InstalledSoftwareSourceKind.PackageProvider,
            SoftwareInventorySourceIds.ForPackageProvider(PackageProviderIds.WinGet, "winget"),
            packageId,
            Version: version,
            ProviderId: PackageProviderIds.WinGet,
            PackageId: packageId,
            PackageSource: "winget");

    private static InventorySourceStatus CompleteProviderSource(string source) =>
        new(
            SoftwareInventorySourceIds.ForPackageProvider(PackageProviderIds.WinGet, source),
            InventorySourceState.Complete);

    private static InventorySourceStatus CompleteRegistry() =>
        new(
            SoftwareInventorySourceIds.WindowsRegistry,
            InventorySourceState.Complete);

    private static InstalledSoftwareSnapshot Snapshot(
        IReadOnlyList<InstalledSoftwareRecord> records,
        IReadOnlyList<InventorySourceStatus> sources) =>
        new(records, sources, DateTimeOffset.UnixEpoch);

    private sealed class FakeCollector(InstalledSoftwareCollectionResult result) : IInstalledSoftwareCollector
    {
        public Task<InstalledSoftwareCollectionResult> CollectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
