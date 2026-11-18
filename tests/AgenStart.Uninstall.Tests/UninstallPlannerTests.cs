using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

internal static class PlanScenarios
{
    public const string CivName = "Sid Meier's Civilization VII";
    public const string CivInstall = @"D:\Games\Sid Meier's Civilization VII";

    /// <summary>A game with a product rule, saves, a running process, shortcuts and an HKCU key.</summary>
    public static (FakeMachine Machine, InstalledProgram Program, InstalledProgram[] Others) Game()
    {
        var machine = new FakeMachine();
        machine.Files
            .File($@"{CivInstall}\Base\Binaries\Win64\CivilizationVII.exe", 120_000_000)
            .File($@"{CivInstall}\Base\Assets\data.pak", 9_000_000_000)
            .File($@"{CivInstall}\unins000.exe", 3_000_000)
            .File($@"C:\Users\alex\AppData\Local\Firaxis Games\{CivName}\Saves\Auto\autosave.Civ7Save", 4_000_000)
            .File($@"C:\Users\alex\AppData\Local\Firaxis Games\{CivName}\Logs\game.log", 50_000)
            .File($@"C:\Users\alex\AppData\Local\Firaxis Games\{CivName}\Cache\shaders.bin", 300_000_000)
            .File($@"C:\Users\alex\Documents\My Games\{CivName}\Options.txt", 2_000)
            .File(@"C:\Users\alex\Desktop\Civilization VII.lnk", 1_000)
            .File(@"C:\Users\alex\Desktop\Notes.lnk", 1_000)
            .File(@"C:\Users\alex\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Firaxis Games\Civilization VII.lnk", 1_000);
        machine.Shortcuts
            .Link(@"C:\Users\alex\Desktop\Civilization VII.lnk", $@"{CivInstall}\Base\Binaries\Win64\CivilizationVII.exe")
            .Link(@"C:\Users\alex\Desktop\Notes.lnk", @"C:\Windows\notepad.exe")
            .Link(@"C:\Users\alex\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Firaxis Games\Civilization VII.lnk", $@"{CivInstall}\Base\Binaries\Win64\CivilizationVII.exe");
        machine.Registry.Key($@"HKCU\Software\Firaxis Games\{CivName}", values: 3);
        machine.Processes.Running(4242, "CivilizationVII.exe", $@"{CivInstall}\Base\Binaries\Win64\CivilizationVII.exe");

        var program = Programs.Create(CivName, "Firaxis Games", CivInstall, $@"""{CivInstall}\unins000.exe""", kind: InstallerKind.Inno, version: "1.2.0");
        return (machine, program, [Programs.Create("Steam", "Valve Corporation", @"C:\Program Files (x86)\Steam")]);
    }

    /// <summary>A suite whose siblings share a publisher folder, with a junction, OneDrive and a shared child.</summary>
    public static (FakeMachine Machine, InstalledProgram Program, InstalledProgram[] Others) Suite()
    {
        var machine = new FakeMachine
        {
            Folders = new FakeKnownFolderResolver(new Dictionary<KnownFolder, string>
            {
                [KnownFolder.OneDrive] = @"C:\Users\alex\OneDrive",
                [KnownFolder.Documents] = @"C:\Users\alex\OneDrive\Documents",
                [KnownFolder.ProgramFiles] = @"C:\Programmes"
            })
        };
        machine.Files
            .File(@"C:\Programmes\Acme\Paint\paint.exe", 40_000_000)
            .File(@"C:\Programmes\Acme\Draw\draw.exe", 30_000_000)
            .File(@"C:\Users\alex\AppData\Local\Acme Software\Acme Paint\cache.db", 10_000)
            .File(@"C:\Users\alex\AppData\Local\Acme Software\Acme Draw\cache.db", 10_000)
            .Directory(@"C:\Users\alex\AppData\Roaming\Acme Paint", reparse: true)
            .File(@"C:\Users\alex\AppData\LocalLow\Acme Paint\Acme Draw\shared.dat", 500)
            .File(@"C:\ProgramData\Acme Software\Acme Paint\license.dat", 800)
            .File(@"C:\Users\alex\OneDrive\Documents\Acme Paint\drawing.acp", 2_000, placeholder: true);
        machine.Registry
            .Key(@"HKCU\Software\Acme Software\Acme Paint")
            .Key(@"HKCU\Software\Acme Software\Acme Draw");

        var program = Programs.Create(
            "Acme Paint 2024",
            "Acme Software Inc.",
            @"C:\Programmes\Acme\Paint",
            "MsiExec.exe /X{0A1B2C3D-0000-4000-8000-00000000AC01}",
            scope: ProgramScope.Machine);
        return (machine, program, [Programs.Create("Acme Draw", "Acme Software", @"C:\Programmes\Acme\Draw")]);
    }

    /// <summary>A generic name in a forbidden location, with a rejected quiet command.</summary>
    public static (FakeMachine Machine, InstalledProgram Program, InstalledProgram[] Others) Hostile()
    {
        var machine = new FakeMachine();
        machine.Files
            .File(@"C:\Program Files (x86)\Steam\steam.exe")
            .File(@"C:\Users\alex\AppData\Local\Launcher\settings.ini")
            .File(@"C:\Users\alex\AppData\Local\Games\Launcher\x.dat");
        machine.Registry.Key(@"HKCU\Software\Games\Launcher");

        var program = Programs.Create(
            "Launcher",
            "Games",
            @"C:\Program Files (x86)\Steam",
            quiet: @"cmd.exe /c rd /s /q ""C:\Program Files (x86)\Steam""",
            uninstall: @"""C:\Windows\System32\cmd.exe"" /c del C:\",
            scope: ProgramScope.User);
        return (machine, program, []);
    }
}

public sealed class UninstallPlannerTests
{
    [Fact]
    public void DescribesTheGameWithoutExecutingAnything()
    {
        var (machine, program, others) = PlanScenarios.Game();
        var plan = machine.Plan(program, others);

        Assert.Equal(OfficialStrategy.InstallerSilentFlags, plan.Official.Strategy);
        Assert.Contains(plan.Leftovers, item => item.Target == PlanScenarios.CivInstall && item.DefaultSelected && item.Confidence.Level == ConfidenceLevel.High);
        Assert.Contains(plan.ProtectedUserData, item => item.Target.EndsWith(@"\Saves", StringComparison.Ordinal));
        Assert.Contains(plan.ProtectedUserData, item => item.Target == $@"C:\Users\alex\Documents\My Games\{PlanScenarios.CivName}");
        Assert.Contains(plan.ProtectedUserData, item => item.Confidence.Reasons.Any(reason => reason.Code == "contains-user-data"));
        Assert.Contains(plan.Leftovers, item => item.Kind == LeftoverKind.Shortcut && item.Target.EndsWith("Civilization VII.lnk", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Leftovers, item => item.Target.EndsWith("Notes.lnk", StringComparison.Ordinal));
        Assert.Contains(plan.Leftovers, item => item.Kind == LeftoverKind.RegistryKey && !item.RequiresElevation);
        Assert.Single(plan.Processes);
    }

    [Fact]
    public void SkipsSharedJunctionedAndUserDataTargets()
    {
        var (machine, program, others) = PlanScenarios.Suite();
        var plan = machine.Plan(program, others);

        Assert.Equal(OfficialStrategy.Msi, plan.Official.Strategy);
        Assert.Equal(@"C:\Windows\System32\msiexec.exe", plan.Official.Executable);
        Assert.Contains(plan.Skipped, skip => skip.Reason == SkipReasons.ReparsePoint && skip.Target == @"C:\Users\alex\AppData\Roaming\Acme Paint");
        Assert.Contains(plan.Skipped, skip => skip.Reason == SkipReasons.Shared && skip.Target == @"C:\Users\alex\AppData\LocalLow\Acme Paint");
        Assert.Contains(plan.Leftovers, item => item.Target == @"C:\Programmes\Acme\Paint"
            && item.Confidence.Reasons.Any(reason => reason.Code == "publisher-shared"));
        Assert.DoesNotContain(plan.Leftovers.Concat(plan.ProtectedUserData), item => item.Target.Contains("Acme Draw", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesGenericNamesAndForbiddenInstallLocations()
    {
        var (machine, program, others) = PlanScenarios.Hostile();
        var plan = machine.Plan(program, others);

        Assert.Equal(OfficialStrategy.Unavailable, plan.Official.Strategy);
        Assert.Empty(plan.Leftovers);
        Assert.Empty(plan.ProtectedUserData);
        Assert.Contains(plan.Skipped, skip => skip.Reason == SkipReasons.Forbidden && skip.Target == @"C:\Program Files (x86)\Steam");
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == UninstallPlanner.NameTooGeneric);
        Assert.Equal(2, plan.Diagnostics.Count(diagnostic => diagnostic.Code == OfficialStrategySelector.CommandRejected));
    }

    [Fact]
    public void WithoutRulesOnlyTheOfficialStepIsShown()
    {
        var (machine, program, others) = PlanScenarios.Game();
        var plan = new UninstallPlanner(machine.Ports, null).Plan(program, [program, .. others], null);

        Assert.Empty(plan.Leftovers);
        Assert.Empty(plan.ProtectedUserData);
        Assert.Equal(OfficialStrategy.InstallerSilentFlags, plan.Official.Strategy);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == UninstallPlanner.RulesUnavailable);
    }

    [Fact]
    public void IsDeterministicAndFingerprintsTheMachineState()
    {
        var (machine, program, others) = PlanScenarios.Game();
        var first = machine.Plan(program, others);
        var second = machine.Plan(program, others);

        Assert.Equal(UninstallPlanJsonWriter.Write(first, UninstallSelection.Default(first)), UninstallPlanJsonWriter.Write(second, UninstallSelection.Default(second)));

        machine.Files.File($@"C:\Users\alex\AppData\Local\Firaxis Games\{PlanScenarios.CivName}\Logs\second.log", 10);
        var changed = machine.Plan(program, others);
        Assert.NotEqual(first.StateFingerprint, changed.StateFingerprint);
        Assert.NotEqual(first.PlanId, changed.PlanId);
    }

    [Fact]
    public void StopsTheSizeWalkAndSaysSo()
    {
        var machine = new FakeMachine();
        for (var index = 0; index <= UninstallPlanner.MaximumWalkEntries; index++)
        {
            machine.Files.File($@"D:\Apps\Bulky Tool\f{index}.bin", 1);
        }

        var plan = machine.Plan(Programs.Create("Bulky Tool", installLocation: @"D:\Apps\Bulky Tool"));

        var item = Assert.Single(plan.Leftovers);
        Assert.True(item.SizeIsPartial);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == UninstallPlanner.SizePartial);
    }

    [Fact]
    public async Task ServiceFailsClosedOnAnInvalidRuleFile()
    {
        var (machine, program, others) = PlanScenarios.Game();
        var service = new UninstallPlanService(machine.Ports, () => UninstallRuleSetLoader.Load("{\"schemaVersion\":\"9.9\"}"), null);

        var plan = await service.CreatePlanAsync(program, [program, .. others], TestContext.Current.CancellationToken);

        Assert.Empty(plan.Leftovers);
        Assert.Equal(UninstallPlanService.RulesInvalid, plan.Diagnostics[0].Code);
    }
}

/// <summary>
/// Safety properties over many generated machines: whatever the names, locations and links, nothing
/// forbidden, shared, reached through a reparse point or owned by the user can be pre-selected.
/// </summary>
public sealed class UninstallPlannerPropertyTests
{
    private static readonly string[] Names = ["Acme Paint", "Launcher", "Data", "Zed Editor", "App", "Common Files", "Windows", "Nimbus Sync 3.2"];
    private static readonly string[] Publishers = ["Acme Software Inc.", "Microsoft Corporation", "Games", "Zed Industries", "", "Nimbus"];

    private static readonly string[] Locations =
    [
        @"C:\", @"D:\", @"C:\Program Files", @"C:\Program Files\{p}", @"C:\Program Files\{v}\{p}", @"C:\Users\alex",
        @"C:\Users\alex\AppData\Local", @"C:\Users\alex\AppData\Local\{p}", @"C:\Windows\{p}", @"C:\Program Files\WindowsApps\{p}",
        @"C:\Users\bob\AppData\Local\{p}", @"D:\Games\{p}", @"C:\Users\alex\OneDrive\{p}", @"C:\ProgramData", @"C:\Program Files (x86)\Steam",
        @"C:\Users\alex\AppData\Local\AgenStart", @"C:\Users"
    ];

    private static readonly string[] Roots =
    [
        @"C:\Users\alex\AppData\Local", @"C:\Users\alex\AppData\Roaming", @"C:\Users\alex\AppData\LocalLow", @"C:\ProgramData",
        @"C:\Users\alex\Documents", @"C:\Users\alex\OneDrive"
    ];

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 150; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NoPlanEverOffersAnUnsafeItem(int seed)
    {
        var random = new Random(seed);
        var folders = new FakeKnownFolderResolver(new Dictionary<KnownFolder, string> { [KnownFolder.OneDrive] = @"C:\Users\alex\OneDrive" });
        var machine = new FakeMachine { Folders = folders };
        var name = Pick(random, Names);
        var publisher = Pick(random, Publishers);
        var vendor = string.IsNullOrEmpty(publisher) ? "Vendor" : publisher.Split(' ')[0];
        var install = Pick(random, Locations).Replace("{p}", name, StringComparison.Ordinal).Replace("{v}", vendor, StringComparison.Ordinal);
        var otherName = Pick(random, Names);
        var otherInstall = random.Next(2) == 0 ? WindowsPath.Combine(install, "Plugins") : $@"D:\Other\{otherName}";

        machine.Files.File(WindowsPath.Combine(install, "app.exe"));
        foreach (var root in Roots)
        {
            var candidates = new[] { WindowsPath.Combine(root, name), WindowsPath.Combine(WindowsPath.Combine(root, publisher.Length == 0 ? "x" : publisher), name) };
            foreach (var candidate in candidates.Where(_ => random.Next(3) > 0))
            {
                var mode = random.Next(5);
                if (mode == 0)
                {
                    machine.Files.Directory(candidate, reparse: true);
                }
                else
                {
                    machine.Files.File(WindowsPath.Combine(candidate, mode == 1 ? $@"{otherName}\x.dat" : "x.dat"), placeholder: mode == 2);
                }
            }
        }

        machine.Files.File(@"C:\Users\alex\Desktop\app.lnk");
        machine.Shortcuts.Link(@"C:\Users\alex\Desktop\app.lnk", WindowsPath.Combine(install, "app.exe"));
        machine.Registry.Key($@"HKCU\Software\{name}");
        if (publisher.Length > 0)
        {
            machine.Registry.Key($@"HKCU\Software\{publisher}\{name}");
        }

        var program = Programs.Create(name, publisher, install, $@"""{install}\app.exe""");
        var other = Programs.Create(otherName + " Other", publisher, otherInstall);
        var plan = machine.Plan(program, [other], dataDirectory: @"D:\AgenStartData");

        var policy = PathSafetyPolicy.Create(folders, TestRules.Frozen, @"D:\AgenStartData");
        var forbidden = folders.All.Select(path => WindowsPath.Normalize(path)!).Concat([@"C:\", @"D:\", @"D:\AgenStartData"]).ToArray();
        var everything = plan.Leftovers.Concat(plan.ProtectedUserData).ToArray();

        Assert.All(plan.Leftovers, item =>
        {
            Assert.NotEqual(DataClass.UserData, item.DataClass);
            Assert.False(WindowsPath.IsSameOrUnder(item.Target, @"C:\Users\alex\OneDrive"), "OneDrive content is only ever protected data.");
        });
        Assert.All(plan.ProtectedUserData, item =>
        {
            Assert.Equal(DataClass.UserData, item.DataClass);
            Assert.False(item.DefaultSelected);
        });

        foreach (var item in everything)
        {
            if (item.DefaultSelected)
            {
                Assert.Equal(ConfidenceLevel.High, item.Confidence.Level);
            }

            if (item.Kind == LeftoverKind.RegistryKey)
            {
                Assert.Equal(SafetyVerdict.Allowed, policy.EvaluateRegistryKey(item.Target));
                continue;
            }

            Assert.Equal(SafetyVerdict.Allowed, policy.EvaluatePath(item.Target));
            Assert.DoesNotContain(forbidden, root => WindowsPath.IsSameOrUnder(root, item.Target));
            Assert.False(WindowsPath.IsSameOrUnder(item.Target, other.InstallLocation!) || WindowsPath.IsSameOrUnder(other.InstallLocation!, item.Target));
            for (var path = item.Target; path is not null && !WindowsPath.IsRoot(path); path = WindowsPath.Parent(path))
            {
                Assert.False(machine.Files.GetEntry(path)?.IsReparsePoint == true, $"{item.Target} is reached through a reparse point.");
            }
        }
    }

    private static string Pick(Random random, string[] values) => values[random.Next(values.Length)];
}
