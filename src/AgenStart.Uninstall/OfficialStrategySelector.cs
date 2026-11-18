using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

/// <summary>
/// Chooses how the vendor's uninstall would run, in the ADR-0004 order: winget, then
/// <c>msiexec /x</c>, then the registered quiet command, then the installer's documented silent
/// flags, then the interactive command. Registered commands are validated (ADR-0004 §3) and a
/// rejected one is reported, never repaired. Nothing here starts a process.
/// </summary>
public static partial class OfficialStrategySelector
{
    public const string CommandRejected = "uninstall.command-rejected";
    public const string NoCommand = "uninstall.no-command";

    /// <summary>The only winget source correlated so far; a package id is never taken from free text.</summary>
    public const string WinGetSource = "winget";

    private static readonly string[] InnoFlags = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"];

    private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe"
    };

    public static OfficialUninstallStep Select(
        InstalledProgram program,
        IKnownFolderResolver folders,
        IFileSystemProbe files,
        List<PlanDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var machine = program.Scope == ProgramScope.Machine;
        if (!string.IsNullOrWhiteSpace(program.WinGetPackageId))
        {
            // Same argument list as WinGetCommandBuilder.BuildUninstall (pinned by a cross-project test).
            string[] arguments =
            [
                "uninstall", "--id", program.WinGetPackageId, "--exact", "--source", WinGetSource, "--disable-interactivity",
                "--scope", machine ? "machine" : "user", "--silent", "--accept-source-agreements"
            ];
            return new OfficialUninstallStep(
                OfficialStrategy.WinGet, "winget", arguments, UninstallCommandLine.Format("winget", arguments), IsSilent: true, machine);
        }

        bool Exists(string path) => WindowsPath.Normalize(path) is { } normalized
            && files.GetEntry(normalized) is { Kind: FileSystemEntryKind.File };

        var uninstall = UninstallCommandLine.Parse(program.UninstallString, Exists);
        var productCode = Guid(program.MsiProductCode) ?? MsiProductCodeFrom(uninstall);
        var windows = WindowsPath.Normalize(folders.Resolve(KnownFolder.Windows));
        if (productCode is not null && windows is not null)
        {
            var msiexec = WindowsPath.Combine(windows, @"System32\msiexec.exe");
            string[] arguments = ["/x", productCode, "/qn", "/norestart"];
            return new OfficialUninstallStep(
                OfficialStrategy.Msi, msiexec, arguments, UninstallCommandLine.Format(msiexec, arguments), IsSilent: true, machine);
        }

        if (program.QuietUninstallString is not null)
        {
            var quiet = Validate(UninstallCommandLine.Parse(program.QuietUninstallString, Exists), program, folders, files, diagnostics, "QuietUninstallString");
            if (quiet is not null)
            {
                return Step(OfficialStrategy.QuietUninstallString, quiet.Executable, quiet.Arguments, silent: true, machine);
            }
        }

        if (program.UninstallString is null)
        {
            diagnostics.Add(new PlanDiagnostic(NoCommand, "The uninstall entry has no uninstall command."));
            return new OfficialUninstallStep(OfficialStrategy.Unavailable, null, [], null, IsSilent: false, machine);
        }

        var validated = Validate(uninstall, program, folders, files, diagnostics, "UninstallString");
        if (validated is null)
        {
            return new OfficialUninstallStep(OfficialStrategy.Unavailable, null, [], null, IsSilent: false, machine);
        }

        switch (program.InstallerKind)
        {
            case InstallerKind.Inno:
                return Step(OfficialStrategy.InstallerSilentFlags, validated.Executable, WithFlags(validated.Arguments, InnoFlags), silent: true, machine);
            case InstallerKind.Nsis:
                var directory = WindowsPath.Normalize(program.InstallLocation) ?? WindowsPath.Parent(WindowsPath.Normalize(validated.Executable)!);
                var nsis = WithFlags(validated.Arguments.Where(argument => !argument.StartsWith("_?=", StringComparison.Ordinal)), ["/S"]);
                return Step(OfficialStrategy.InstallerSilentFlags, validated.Executable, [.. nsis, "_?=" + directory], silent: true, machine);
            default:
                return Step(OfficialStrategy.Interactive, validated.Executable, validated.Arguments, silent: false, machine);
        }
    }

    private static ParsedCommand? Validate(
        ParsedCommand? command,
        InstalledProgram program,
        IKnownFolderResolver folders,
        IFileSystemProbe files,
        List<PlanDiagnostic> diagnostics,
        string source)
    {
        ParsedCommand? Reject(string why)
        {
            diagnostics.Add(new PlanDiagnostic(CommandRejected, $"{source} rejected: {why}."));
            return null;
        }

        if (command is null)
        {
            return Reject("it cannot be parsed");
        }

        var executable = WindowsPath.Normalize(command.Executable);
        if (executable is null)
        {
            return Reject("the program path is not absolute");
        }

        if (BlockedHosts.Contains(WindowsPath.FileName(executable)))
        {
            return Reject($"{WindowsPath.FileName(executable)} is a script host or shell");
        }

        if (files.GetEntry(executable) is not { Kind: FileSystemEntryKind.File })
        {
            return Reject("the program does not exist");
        }

        var windows = WindowsPath.Normalize(folders.Resolve(KnownFolder.Windows));
        var allowedRoots = new[]
        {
            WindowsPath.Normalize(program.InstallLocation),
            WindowsPath.Normalize(folders.Resolve(KnownFolder.ProgramFiles)),
            WindowsPath.Normalize(folders.Resolve(KnownFolder.ProgramFilesX86)),
            windows is null ? null : WindowsPath.Combine(windows, "Installer")
        };
        var installLocationIsRoot = WindowsPath.Normalize(program.InstallLocation) is { } location && WindowsPath.IsRoot(location);
        if (installLocationIsRoot || !allowedRoots.Any(root => root is not null && WindowsPath.IsStrictlyUnder(executable, root)))
        {
            return Reject("the program is outside the install location, Program Files and the Windows Installer cache");
        }

        return command with { Executable = executable };
    }

    private static OfficialUninstallStep Step(OfficialStrategy strategy, string executable, IReadOnlyList<string> arguments, bool silent, bool machine) =>
        new(strategy, executable, arguments, UninstallCommandLine.Format(executable, arguments), silent, machine);

    private static IReadOnlyList<string> WithFlags(IEnumerable<string> arguments, IEnumerable<string> flags)
    {
        var result = arguments.ToList();
        foreach (var flag in flags)
        {
            if (!result.Any(argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(flag);
            }
        }

        return result;
    }

    private static string? MsiProductCodeFrom(ParsedCommand? command)
    {
        if (command is null || !string.Equals(WindowsPath.FileName(command.Executable.Replace('/', '\\')), "msiexec.exe", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.Executable, "msiexec", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        for (var index = 0; index < command.Arguments.Count; index++)
        {
            var argument = command.Arguments[index];
            if (argument.Length < 2 || argument[0] is not ('/' or '-') || char.ToUpperInvariant(argument[1]) is not ('I' or 'X'))
            {
                continue;
            }

            var attached = argument[2..];
            var code = attached.Length > 0
                ? Guid(attached)
                : index + 1 < command.Arguments.Count ? Guid(command.Arguments[index + 1]) : null;
            if (code is not null)
            {
                return code;
            }
        }

        return null;
    }

    private static string? Guid(string? value) =>
        value is not null && ProductCode().IsMatch(value.Trim()) ? value.Trim().ToUpperInvariant() : null;

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex ProductCode();
}
