using System.Reflection;
using AgenStart.Uninstall;
using static AgenStart.Uninstall.Tests.RegistryFixtures;

namespace AgenStart.Uninstall.Tests;

public sealed class ProgramNameNormalizerTests
{
    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "microsoft visual c++ 2015-2022 redistributable")]
    [InlineData("  7-Zip   23.01 (64-bit) ", "7-zip 23.01")]
    [InlineData("Logiciel Élève®", "logiciel eleve")]
    [InlineData("Tool™ [x86]", "tool")]
    [InlineData("Tool (beta)", "tool (beta)")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void FoldsCaseAccentsMarksSpacingAndArchitecture(string? input, string expected)
    {
        Assert.Equal(expected, ProgramNameNormalizer.Normalize(input));
    }
}

public sealed class InstalledProgramIdTests
{
    [Fact]
    public void IsStableAndCaseInsensitiveOnTheKeyPath()
    {
        var first = InstalledProgramId.Create(ProgramSource.Registry, @"HKEY_LOCAL_MACHINE\SOFTWARE\X\App");
        var second = InstalledProgramId.Create(ProgramSource.Registry, @" hkey_local_machine\software\x\app ");

        Assert.Equal(first, second);
        Assert.StartsWith("registry:", first, StringComparison.Ordinal);
        Assert.Equal("registry:".Length + 32, first.Length);
    }

    [Fact]
    public void DependsOnSourceAndKeyPath()
    {
        var registry = InstalledProgramId.Create(ProgramSource.Registry, "key");
        var winget = InstalledProgramId.Create(ProgramSource.WinGet, "key");

        Assert.NotEqual(registry, InstalledProgramId.Create(ProgramSource.Registry, "other"));
        Assert.NotEqual(registry["registry:".Length..], winget["winget:".Length..]);
    }

    [Fact]
    public void KnownValueDoesNotDrift()
    {
        // Ids may be stored by later phases; the derivation must never change silently.
        var expected = "registry:" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData("registry|HKEY_CURRENT_USER\\X"u8))[..32];

        Assert.Equal(expected, InstalledProgramId.Create(ProgramSource.Registry, @"hkey_current_user\x"));
        Assert.Equal("registry", InstalledProgramId.SourceToken(ProgramSource.Registry));
        Assert.Equal("winget", InstalledProgramId.SourceToken(ProgramSource.WinGet));
        Assert.Equal("msix", InstalledProgramId.SourceToken(ProgramSource.Msix));
    }
}

public sealed class InstallerKindDetectorTests
{
    private const string ProductCode = "{11111111-2222-3333-4444-555555555555}";

    [Fact]
    public void WindowsInstallerFlagWithGuidKeyIsMsiWithCanonicalProductCode()
    {
        var entry = Entry(Machine64, ProductCode.ToLowerInvariant(), "App") with { WindowsInstaller = true };

        Assert.Equal(new InstallerDetection(InstallerKind.Msi, ProductCode), InstallerKindDetector.Detect(entry));
    }

    [Fact]
    public void GuidKeyWithMsiexecCommandIsMsi()
    {
        var entry = Entry(Machine64, ProductCode, "App", $"MsiExec.exe /I{ProductCode}");

        Assert.Equal(InstallerKind.Msi, InstallerKindDetector.Detect(entry).Kind);
    }

    [Fact]
    public void MsiProductCodeFallsBackToTheUninstallCommand()
    {
        var entry = Entry(Machine64, "NotAGuid", "App", $"MsiExec.exe /X{ProductCode.ToLowerInvariant()}") with { WindowsInstaller = true };

        Assert.Equal(new InstallerDetection(InstallerKind.Msi, ProductCode), InstallerKindDetector.Detect(entry));
    }

    [Fact]
    public void GuidKeyWithoutMsiMarkerIsNotMsi()
    {
        var entry = Entry(Machine64, ProductCode, "App", @"C:\App\uninstall.exe");

        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(entry).Kind);
    }

    [Fact]
    public void InnoSetupIsRecognisedByValueOrKeySuffix()
    {
        Assert.Equal(InstallerKind.Inno, InstallerKindDetector.Detect(Entry(Machine64, "App", "App") with { InnoSetupAppPath = @"C:\App" }).Kind);
        Assert.Equal(InstallerKind.Inno, InstallerKindDetector.Detect(Entry(Machine64, "{ABC}_is1", "App")).Kind);
    }

    [Fact]
    public void NsisIsRecognisedOnlyByItsOwnValues()
    {
        var nsis = Entry(Machine64, "App", "App") with { ValueNames = ["DisplayName", "NSIS:StartMenuDir"] };
        var plain = Entry(Machine64, "App", "App", @"C:\App\Uninstall.exe") with { ValueNames = ["DisplayName", "UninstallString"] };

        Assert.Equal(InstallerKind.Nsis, InstallerKindDetector.Detect(nsis).Kind);
        Assert.Equal(InstallerKind.Unknown, InstallerKindDetector.Detect(plain).Kind);
    }
}

public sealed class UninstallCapabilitiesTests
{
    private static InstalledProgram Program(InstallerKind kind, string? productCode = null, string? quiet = null) => new()
    {
        Id = "registry:x",
        DisplayName = "App",
        Scope = ProgramScope.Machine,
        Source = ProgramSource.Registry,
        InstallerKind = kind,
        MsiProductCode = productCode,
        QuietUninstallString = quiet
    };

    [Fact]
    public void SilentUninstallIsLikelyOnlyWithAKnownMechanism()
    {
        Assert.True(UninstallCapabilities.IsSilentUninstallLikely(Program(InstallerKind.Unknown, quiet: "x /S")));
        Assert.True(UninstallCapabilities.IsSilentUninstallLikely(Program(InstallerKind.Msi, "{A}")));
        Assert.True(UninstallCapabilities.IsSilentUninstallLikely(Program(InstallerKind.Inno)));
        Assert.True(UninstallCapabilities.IsSilentUninstallLikely(Program(InstallerKind.Nsis)));
        Assert.False(UninstallCapabilities.IsSilentUninstallLikely(Program(InstallerKind.Msi)));
        Assert.False(UninstallCapabilities.IsSilentUninstallLikely(Program(InstallerKind.Unknown)));
    }
}

public sealed class ReadPortContractTests
{
    [Theory]
    [InlineData(typeof(IInstalledProgramInventory), "CaptureAsync")]
    [InlineData(typeof(IUninstallRegistrySource), "ReadAll")]
    [InlineData(typeof(IKnownFolderResolver), "Resolve")]
    [InlineData(typeof(IFileSystemProbe), "GetEntry", "ListChildren")]
    [InlineData(typeof(IRegistryProbe), "GetKey")]
    [InlineData(typeof(IShortcutResolver), "ResolveTarget")]
    [InlineData(typeof(IProcessInspector), "ListProcesses")]
    [InlineData(typeof(IUninstallPlanSource), "CreatePlanAsync")]
    [InlineData(typeof(IRunningProcessDetector), "Detect")]
    public void ReadPortsExposeOnlyTheirReadMethod(Type port, params string[] readMethods)
    {
        var methods = port.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(method => method.Name).Order(StringComparer.Ordinal);

        Assert.Equal(readMethods, methods);
    }

    [Fact]
    public void UninstallCoreDeclaresNoOtherPort()
    {
        // P3 deliberately adds three acting ports (official uninstall, process closing, journal) and
        // one read port (Restart Manager detection). Any other port is a design change.
        var interfaces = typeof(IInstalledProgramInventory).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "IFileSystemProbe", "IInstalledProgramInventory", "IKnownFolderResolver", "IOfficialUninstaller", "IProcessInspector",
                "IRegistryProbe", "IRunningProcessCloser", "IRunningProcessDetector", "IShortcutResolver", "IUninstallJournal",
                "IUninstallPlanSource", "IUninstallRegistrySource"
            ],
            interfaces);
    }
}
