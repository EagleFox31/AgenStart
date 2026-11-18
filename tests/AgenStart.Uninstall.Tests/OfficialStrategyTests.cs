using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

public sealed class UninstallCommandLineTests
{
    [Fact]
    public void ParsesAQuotedProgramAndItsArguments()
    {
        var parsed = UninstallCommandLine.Parse(@"""C:\Program Files\App\unins000.exe"" /LOG ""C:\a b.txt""", _ => false);

        Assert.NotNull(parsed);
        Assert.Equal(@"C:\Program Files\App\unins000.exe", parsed.Executable);
        Assert.Equal(["/LOG", @"C:\a b.txt"], parsed.Arguments);
    }

    [Fact]
    public void ResolvesAnUnquotedPathWithSpacesLikeCreateProcess()
    {
        var parsed = UninstallCommandLine.Parse(
            @"C:\Program Files\My App\uninstall.exe /S",
            path => path == @"C:\Program Files\My App\uninstall.exe");

        Assert.NotNull(parsed);
        Assert.Equal(@"C:\Program Files\My App\uninstall.exe", parsed.Executable);
        Assert.Equal(["/S"], parsed.Arguments);
    }

    [Fact]
    public void SplitsArgumentsWithTheWindowsRules()
    {
        Assert.Equal(["a", "b c", @"d\""e", @"f\\"], UninstallCommandLine.SplitArguments(@"a ""b c"" d\\\""e f\\"));
    }

    [Fact]
    public void FormatsWithoutQuotingTheNsisDirectoryArgument()
    {
        Assert.Equal(
            @"""C:\Program Files\App\uninst.exe"" /S _?=C:\Program Files\App",
            UninstallCommandLine.Format(@"C:\Program Files\App\uninst.exe", ["/S", @"_?=C:\Program Files\App"]));
    }
}

public sealed class OfficialStrategySelectorTests
{
    private const string Install = @"C:\Program Files\App";

    private static (OfficialUninstallStep Step, List<PlanDiagnostic> Diagnostics) Select(InstalledProgram program, params string[] existingFiles)
    {
        var files = new InMemoryFileSystemProbe();
        foreach (var file in existingFiles)
        {
            files.File(file);
        }

        var diagnostics = new List<PlanDiagnostic>();
        return (OfficialStrategySelector.Select(program, new FakeKnownFolderResolver(), files, diagnostics), diagnostics);
    }

    [Fact]
    public void PrefersWinGet()
    {
        var (step, _) = Select(Programs.Create("App", winget: "Vendor.App", msi: "{11111111-2222-3333-4444-555555555555}"));

        Assert.Equal(OfficialStrategy.WinGet, step.Strategy);
        Assert.Equal("winget uninstall --id Vendor.App --exact --silent --disable-interactivity", step.CommandPreview);
        Assert.True(step.IsSilent);
    }

    [Theory]
    [InlineData("{11111111-2222-3333-4444-555555555555}", null)]
    [InlineData(null, "MsiExec.exe /X{11111111-2222-3333-4444-555555555555}")]
    [InlineData(null, "msiexec /i {11111111-2222-3333-4444-555555555555}")]
    public void ThenMsiexecWithTheProductCode(string? productCode, string? uninstall)
    {
        var (step, _) = Select(Programs.Create("App", msi: productCode, uninstall: uninstall));

        Assert.Equal(OfficialStrategy.Msi, step.Strategy);
        Assert.Equal(@"C:\Windows\System32\msiexec.exe", step.Executable);
        Assert.Equal(["/x", "{11111111-2222-3333-4444-555555555555}", "/qn", "/norestart"], step.Arguments);
    }

    [Fact]
    public void ThenTheValidatedQuietCommand()
    {
        var (step, _) = Select(
            Programs.Create("App", installLocation: Install, uninstall: $@"""{Install}\u.exe""", quiet: $@"""{Install}\u.exe"" /quiet"),
            $@"{Install}\u.exe");

        Assert.Equal(OfficialStrategy.QuietUninstallString, step.Strategy);
        Assert.Equal(["/quiet"], step.Arguments);
    }

    [Fact]
    public void ThenInnoSilentFlags()
    {
        var (step, _) = Select(Programs.Create("App", installLocation: Install, uninstall: $@"""{Install}\unins000.exe""", kind: InstallerKind.Inno), $@"{Install}\unins000.exe");

        Assert.Equal(OfficialStrategy.InstallerSilentFlags, step.Strategy);
        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"], step.Arguments);
    }

    [Fact]
    public void ThenNsisSilentFlagWithTheInstallDirectory()
    {
        var (step, _) = Select(Programs.Create("App", installLocation: Install, uninstall: $@"""{Install}\uninst.exe""", kind: InstallerKind.Nsis), $@"{Install}\uninst.exe");

        Assert.Equal(OfficialStrategy.InstallerSilentFlags, step.Strategy);
        Assert.Equal(["/S", $"_?={Install}"], step.Arguments);
    }

    [Fact]
    public void ThenTheInteractiveCommand()
    {
        var (step, diagnostics) = Select(Programs.Create("App", installLocation: Install, uninstall: $@"""{Install}\setup.exe"" /remove"), $@"{Install}\setup.exe");

        Assert.Equal(OfficialStrategy.Interactive, step.Strategy);
        Assert.False(step.IsSilent);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(@"cmd.exe /c del C:\x", @"C:\Windows\System32\cmd.exe")]
    [InlineData(@"""C:\Windows\System32\cmd.exe"" /c rd /s C:\", @"C:\Windows\System32\cmd.exe")]
    [InlineData(@"""C:\Windows\System32\rundll32.exe"" x.dll,Uninstall", @"C:\Windows\System32\rundll32.exe")]
    [InlineData(@"""C:\Program Files\App\missing.exe""", @"C:\Program Files\App\other.exe")]
    [InlineData(@"""C:\Users\alex\Downloads\evil.exe""", @"C:\Users\alex\Downloads\evil.exe")]
    [InlineData(@"""C:\ProgramData\Package Cache\{x}\setup.exe"" /uninstall", @"C:\ProgramData\Package Cache\{x}\setup.exe")]
    [InlineData(@"uninstall.exe", @"C:\Program Files\App\uninstall.exe")]
    public void RejectsUnsafeCommandsAndReportsWhy(string uninstall, string existing)
    {
        var (step, diagnostics) = Select(Programs.Create("App", installLocation: Install, uninstall: uninstall), existing);

        Assert.Equal(OfficialStrategy.Unavailable, step.Strategy);
        Assert.Null(step.CommandPreview);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == OfficialStrategySelector.CommandRejected);
    }

    [Fact]
    public void RejectsAnyCommandWhenTheInstallLocationIsADriveRoot()
    {
        var (step, _) = Select(Programs.Create("App", installLocation: @"C:\", uninstall: @"""C:\Program Files\App\u.exe"""), @"C:\Program Files\App\u.exe");

        Assert.Equal(OfficialStrategy.Unavailable, step.Strategy);
    }

    [Fact]
    public void ReportsAMissingCommand()
    {
        var (step, diagnostics) = Select(Programs.Create("App"));

        Assert.Equal(OfficialStrategy.Unavailable, step.Strategy);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == OfficialStrategySelector.NoCommand);
    }
}

public sealed class ShellLinkParserTests
{
    [Fact]
    public void ReadsTheLocalTargetOfAShortcut()
    {
        Assert.Equal(@"C:\Program Files\App\app.exe", ShellLinkParser.ReadTarget(Link(@"C:\Program Files\App\app.exe")));
    }

    [Fact]
    public void ReturnsNullForAnythingElse()
    {
        Assert.Null(ShellLinkParser.ReadTarget([]));
        Assert.Null(ShellLinkParser.ReadTarget(new byte[0x4C]));
        var truncated = Link(@"C:\App\a.exe");
        Assert.Null(ShellLinkParser.ReadTarget(truncated.AsSpan(0, 0x50)));
    }

    /// <summary>A minimal MS-SHLLINK file: header, no IDList, a LinkInfo with an ANSI local base path.</summary>
    internal static byte[] Link(string target)
    {
        var path = System.Text.Encoding.Latin1.GetBytes(target + "\0");
        var bytes = new byte[0x4C + 0x1C + path.Length + 1];
        BitConverter.GetBytes(0x4C).CopyTo(bytes, 0);
        new Guid("00021401-0000-0000-C000-000000000046").ToByteArray().CopyTo(bytes, 4);
        BitConverter.GetBytes(0x2u).CopyTo(bytes, 0x14);

        var info = 0x4C;
        BitConverter.GetBytes(0x1C + path.Length + 1).CopyTo(bytes, info);
        BitConverter.GetBytes(0x1C).CopyTo(bytes, info + 4);
        BitConverter.GetBytes(0x1u).CopyTo(bytes, info + 8);
        BitConverter.GetBytes(0x1C).CopyTo(bytes, info + 0x10);
        BitConverter.GetBytes(0x1C + path.Length).CopyTo(bytes, info + 0x18);
        path.CopyTo(bytes, info + 0x1C);
        return bytes;
    }
}
