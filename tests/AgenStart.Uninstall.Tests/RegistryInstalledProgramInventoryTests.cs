using AgenStart.Uninstall;
using static AgenStart.Uninstall.Tests.RegistryFixtures;

namespace AgenStart.Uninstall.Tests;

public sealed class RegistryInstalledProgramInventoryTests
{
    [Fact]
    public async Task ReadsMachineBothViewsAndUserAsComplete()
    {
        var source = new FakeUninstallRegistrySource(
            Probe(Machine64, Entry(Machine64, "Zeta", "Zeta Tool") with { Publisher = "Zeta Inc", DisplayVersion = "2.0" }),
            Probe(Machine32, Entry(Machine32, "Alpha", "Alpha Editor")),
            Probe(User64, Entry(User64, "Mid", "Mid Player")),
            Probe(User32));

        var result = await new RegistryInstalledProgramInventory(source).CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(InstalledProgramInventoryStatus.Complete, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["Alpha Editor", "Mid Player", "Zeta Tool"], result.Programs.Select(program => program.DisplayName));

        var zeta = result.Programs[2];
        Assert.Equal(ProgramScope.Machine, zeta.Scope);
        Assert.Equal(ProgramSource.Registry, zeta.Source);
        Assert.Equal("Zeta Inc", zeta.Publisher);
        Assert.Equal("2.0", zeta.Version);
        Assert.Equal(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Zeta", zeta.RegistryKeyPath);
        Assert.Equal(ProgramScope.User, result.Programs[1].Scope);
        Assert.Null(zeta.WinGetPackageId);
    }

    [Fact]
    public void UnreadableLocationMakesTheResultPartialAndKeepsWhatWasRead()
    {
        var result = RegistryInstalledProgramInventory.Build(
        [
            Probe(Machine64, Entry(Machine64, "App", "App")),
            new UninstallRegistryProbeResult(Machine32, [], RootUnreadable: true),
            new UninstallRegistryProbeResult(User64, [Entry(User64, "Other", "Other")], UnreadableKeyCount: 2),
            Probe(User32)
        ]);

        Assert.Equal(InstalledProgramInventoryStatus.Partial, result.Status);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(RegistryInstalledProgramInventory.PartialReadCode, diagnostic.Code));
        Assert.Contains("HKEY_LOCAL_MACHINE (32-bit view)", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("HKEY_CURRENT_USER (64-bit view)", result.Diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Equal(["App", "Other"], result.Programs.Select(program => program.DisplayName));
    }

    [Fact]
    public void UnreadableSecondaryValuesMakeTheResultPartial()
    {
        var result = RegistryInstalledProgramInventory.Build(
            [Probe(Machine64, Entry(Machine64, "App", "App") with { HasUnreadableValues = true })]);

        Assert.Equal(InstalledProgramInventoryStatus.Partial, result.Status);
        Assert.Single(result.Programs);
    }

    [Fact]
    public void SharedUserKeySeenThroughBothViewsIsListedOnce()
    {
        var result = RegistryInstalledProgramInventory.Build(
        [
            Probe(User64, Entry(User64, "Tool", "Tool")),
            Probe(User32, Entry(User32, "Tool", "Tool"))
        ]);

        Assert.Single(result.Programs);
    }

    [Fact]
    public void SameMsiProductInBothMachineViewsKeepsTheFirstSighting()
    {
        const string ProductCode = "{11111111-2222-3333-4444-555555555555}";
        var msi64 = Entry(Machine64, ProductCode, "Runtime (x64)", $"MsiExec.exe /X{ProductCode}") with { DisplayVersion = "1.0" };
        var msi32 = Entry(Machine32, ProductCode, "Runtime", $"MsiExec.exe /X{ProductCode}") with { DisplayVersion = "1.0" };

        var result = RegistryInstalledProgramInventory.Build([Probe(Machine64, msi64), Probe(Machine32, msi32)]);

        var program = Assert.Single(result.Programs);
        Assert.Equal(msi64.KeyPath, program.RegistryKeyPath);
    }

    [Fact]
    public void SameNameWithoutSharedIdentityStaysSeparate()
    {
        var result = RegistryInstalledProgramInventory.Build(
        [
            Probe(Machine64, Entry(Machine64, "A", "Tool", @"C:\A\uninstall.exe")),
            Probe(User64, Entry(User64, "B", "Tool", @"C:\B\uninstall.exe"))
        ]);

        Assert.Equal(2, result.Programs.Count);
    }

    [Fact]
    public void SameNameAndUninstallCommandAcrossHivesIsOneProgram()
    {
        var result = RegistryInstalledProgramInventory.Build(
        [
            Probe(Machine64, Entry(Machine64, "A", "Tool", @"""C:\Tool\uninstall.exe""")),
            Probe(User64, Entry(User64, "B", "TOOL", @"c:\tool\uninstall.exe"))
        ]);

        Assert.Equal(ProgramScope.Machine, Assert.Single(result.Programs).Scope);
    }

    [Fact]
    public void DifferentVersionsAreKeptSideBySide()
    {
        var result = RegistryInstalledProgramInventory.Build(
        [
            Probe(
                Machine64,
                Entry(Machine64, "A", "Tool") with { DisplayVersion = "1.0" },
                Entry(Machine64, "B", "Tool") with { DisplayVersion = "2.0" })
        ]);

        Assert.Equal(["1.0", "2.0"], result.Programs.Select(program => program.Version));
    }

    [Fact]
    public void MapsSizeDateLocationAndInstallerDetails()
    {
        var entry = Entry(Machine64, "App_is1", "  App  ") with
        {
            InstallLocation = @"C:\Program Files\App\",
            QuietUninstallString = @"""C:\Program Files\App\unins000.exe"" /SILENT",
            EstimatedSizeKilobytes = 2048,
            InstallDate = "20240131"
        };

        var program = RegistryInstalledProgramInventory.Map(entry);

        Assert.Equal("App", program.DisplayName);
        Assert.Equal(2048L * 1024, program.EstimatedSizeBytes);
        Assert.Equal(new DateOnly(2024, 1, 31), program.InstallDate);
        Assert.Equal(@"C:\Program Files\App\", program.InstallLocation);
        Assert.Equal(InstallerKind.Inno, program.InstallerKind);
        Assert.Equal(InstalledProgramId.Create(ProgramSource.Registry, entry.KeyPath), program.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2024-01-31")]
    [InlineData("20241399")]
    public void IgnoresMissingOrMalformedInstallDate(string? installDate)
    {
        var program = RegistryInstalledProgramInventory.Map(Entry(Machine64, "App", "App") with { InstallDate = installDate });

        Assert.Null(program.InstallDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void IgnoresMissingOrNonPositiveSize(long? kilobytes)
    {
        var program = RegistryInstalledProgramInventory.Map(Entry(Machine64, "App", "App") with { EstimatedSizeKilobytes = kilobytes });

        Assert.Null(program.EstimatedSizeBytes);
    }

    [Fact]
    public void FiltersHiddenUpdatesChildrenAndUnremovableEntries()
    {
        var result = RegistryInstalledProgramInventory.Build(
        [
            Probe(
                Machine64,
                Entry(Machine64, "Kept", "Kept"),
                Entry(Machine64, "Hidden", "Hidden") with { SystemComponent = true },
                Entry(Machine64, "Blank", "   "),
                Entry(Machine64, "Child", "Child") with { ParentKeyName = "Kept" },
                Entry(Machine64, "KB1", "Security Update for X") with { ReleaseType = "security update" },
                Entry(Machine64, "NoCommand", "No Command", uninstallString: null),
                Entry(Machine64, "MsiOnly", "Msi Only", uninstallString: null) with { WindowsInstaller = true })
        ]);

        Assert.Equal(["Kept", "Msi Only"], result.Programs.Select(program => program.DisplayName));
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var source = new FakeUninstallRegistrySource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RegistryInstalledProgramInventory(source).CaptureAsync(cancellation.Token));
        Assert.Equal(0, source.ReadCount);
    }
}
