using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.Uninstall;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Tests;

// Every test here uses fake runners, fake process control and fake Restart Manager queries:
// nothing starts, closes or ends a real process.
public sealed class OfficialUninstallerTests
{
    private const string ProductCode = "{11111111-2222-3333-4444-555555555555}";
    private const string Msiexec = @"C:\Windows\System32\msiexec.exe";
    private const string Unins = @"C:\Program Files\App\unins000.exe";

    [Theory]
    [InlineData(0, PackageOperationStatus.Succeeded, false)]
    [InlineData(3010, PackageOperationStatus.RebootRequired, false)]
    [InlineData(1641, PackageOperationStatus.RebootRequired, false)]
    [InlineData(1618, PackageOperationStatus.Failed, true)]
    [InlineData(1602, PackageOperationStatus.CancelledByUser, false)]
    [InlineData(1223, PackageOperationStatus.CancelledByUser, false)]
    [InlineData(1603, PackageOperationStatus.Failed, false)]
    public async Task A_user_msi_runs_msiexec_with_an_argument_list_and_normalises_the_exit_code(int exitCode, PackageOperationStatus status, bool retryable)
    {
        var machine = new Machine();
        var program = Msi(ProgramScope.User);
        machine.Runner.Result = new UninstallProcessResult(true, exitCode);

        var result = await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal(exitCode, result.ExitCode);
        var call = Assert.Single(machine.Runner.Calls);
        Assert.Equal(Msiexec, call.Executable);
        Assert.Equal(["/x", ProductCode, "/qn", "/norestart"], call.Arguments);
    }

    [Fact]
    public async Task A_machine_msi_without_elevation_runs_nothing()
    {
        var machine = new Machine();
        var program = Msi(ProgramScope.Machine);

        var result = await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.RequiresElevation, result.Status);
        Assert.Equal(UninstallExitCodes.ElevationRequiredCode, result.DiagnosticCode);
        Assert.False(result.Executed);
        Assert.Empty(machine.Runner.Calls);

        machine.Elevated = true;
        await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);
        Assert.Single(machine.Runner.Calls);
    }

    [Fact]
    public async Task Shell_metacharacters_reach_the_uninstaller_as_literal_arguments()
    {
        var machine = new Machine();
        var program = Registry(quiet: $"\"{Unins}\" /SILENT \"a&b|c\" >out");

        await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);

        var call = Assert.Single(machine.Runner.Calls);
        Assert.Equal(Unins, call.Executable);
        Assert.Equal(["/SILENT", "a&b|c", ">out"], call.Arguments);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe /c del C:\x")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -c x")]
    [InlineData(@"C:\Windows\System32\rundll32.exe x.dll,Entry")]
    [InlineData(@"C:\Temp\evil.exe /S")]
    [InlineData(@"unins000.exe /S")]
    public async Task Refused_commands_are_never_run(string command)
    {
        var machine = new Machine();
        var program = Registry(quiet: null, uninstall: command);
        var step = machine.Select(program);

        var result = await machine.Uninstaller().UninstallAsync(program, step, TestContext.Current.CancellationToken);

        Assert.Equal(OfficialStrategy.Unavailable, step.Strategy);
        Assert.Equal(OfficialStrategySelector.CommandRejected, result.DiagnosticCode);
        Assert.False(result.Executed);
        Assert.Empty(machine.Runner.Calls);
    }

    [Fact]
    public async Task A_command_that_changed_since_review_is_not_run()
    {
        var machine = new Machine();
        var reviewed = machine.Select(Registry(quiet: $"\"{Unins}\" /SILENT"));
        var current = Registry(quiet: $"\"{Unins}\" /SILENT /DELETEALL");

        var result = await machine.Uninstaller().UninstallAsync(current, reviewed, TestContext.Current.CancellationToken);

        Assert.Equal(UninstallExitCodes.ChangedSinceReviewCode, result.DiagnosticCode);
        Assert.False(result.Executed);
        Assert.Empty(machine.Runner.Calls);
    }

    [Theory]
    [InlineData(740, PackageOperationStatus.RequiresElevation, "uninstall.requires-elevation")]
    [InlineData(2, PackageOperationStatus.Failed, "uninstall.start-failed")]
    public async Task Start_errors_are_reported_without_retrying(int error, PackageOperationStatus status, string code)
    {
        var machine = new Machine();
        var program = Registry(quiet: $"\"{Unins}\" /SILENT");
        machine.Runner.Result = new UninstallProcessResult(false, null, StartErrorCode: error);

        var result = await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(code, result.DiagnosticCode);
        Assert.Single(machine.Runner.Calls);
    }

    [Fact]
    public async Task A_timeout_stops_waiting_and_is_reported()
    {
        var machine = new Machine();
        var program = Registry(quiet: $"\"{Unins}\" /SILENT");
        machine.Runner.Result = new UninstallProcessResult(true, null, TimedOut: true);

        var result = await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.TimedOut, result.Status);
        Assert.Equal(UninstallExitCodes.TimedOutCode, result.DiagnosticCode);
    }

    [Theory]
    [InlineData(PackageOperationStatus.Succeeded, "winget.success", PackageOperationStatus.Succeeded, false)]
    [InlineData(PackageOperationStatus.NotFound, "winget.package-not-found", PackageOperationStatus.NotFound, false)]
    [InlineData(PackageOperationStatus.Failed, "winget.install-in-progress", PackageOperationStatus.Failed, true)]
    [InlineData(PackageOperationStatus.CancelledByUser, "winget.cancelled", PackageOperationStatus.CancelledByUser, false)]
    public async Task WinGet_entries_go_through_the_package_provider(
        PackageOperationStatus providerStatus, string code, PackageOperationStatus status, bool retryable)
    {
        var machine = new Machine();
        var program = WinGet(ProgramScope.Machine);
        machine.WinGet.Result = providerStatus;
        machine.WinGet.Code = code;
        var step = machine.Select(program);

        var result = await machine.Uninstaller().UninstallAsync(program, step, TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(retryable, result.Retryable);
        Assert.Empty(machine.Runner.Calls);
        var request = Assert.Single(machine.WinGet.Requests);
        Assert.Equal("Vendor.App", request.Package.PackageId);
        Assert.Equal(PackageScope.Machine, request.Package.ScopePreference);
        Assert.Equal(step.Arguments, WinGetCommandBuilder.BuildUninstall(request).Arguments);
    }

    [Theory]
    [InlineData(ProgramScope.User)]
    [InlineData(ProgramScope.Machine)]
    public void The_reviewed_winget_preview_matches_the_command_the_provider_runs(ProgramScope scope)
    {
        var step = new Machine().Select(WinGet(scope));
        var request = new PackageUninstallRequest(
            "Vendor.App",
            new ProviderPackageReference(PackageProviderIds.WinGet, "Vendor.App", OfficialStrategySelector.WinGetSource,
                scope == ProgramScope.Machine ? PackageScope.Machine : PackageScope.User));

        Assert.Equal(step.Arguments, WinGetCommandBuilder.BuildUninstall(request).Arguments);
    }

    [Fact]
    public async Task Without_a_winget_provider_nothing_runs()
    {
        var machine = new Machine { WithWinGet = false };
        var program = WinGet(ProgramScope.User);

        var result = await machine.Uninstaller().UninstallAsync(program, machine.Select(program), TestContext.Current.CancellationToken);

        Assert.Equal(PackageOperationStatus.ProviderUnavailable, result.Status);
        Assert.False(result.Executed);
        Assert.Empty(machine.Runner.Calls);
    }

    [Fact]
    public async Task The_real_runner_refuses_a_relative_path_before_starting_anything()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new UninstallProcessRunner().RunAsync("unins000.exe", [], TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Processes_are_not_touched_without_consent()
    {
        var control = new FakeProcessControl();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WindowsProcessCloser(control, TimeSpan.Zero).CloseAsync([Process(1)], ProcessCloseConsent.None, TestContext.Current.CancellationToken));

        Assert.Empty(control.Log);
    }

    [Fact]
    public async Task Closing_asks_each_window_and_ends_a_process_only_with_the_second_approval()
    {
        var control = new FakeProcessControl { Exits = [1] };
        var closer = new WindowsProcessCloser(control, TimeSpan.Zero);

        var gentle = await closer.CloseAsync([Process(1), Process(2)], new ProcessCloseConsent(CloseWindows: true), TestContext.Current.CancellationToken);

        Assert.Equal([1], gentle.Closed.Select(process => process.ProcessId));
        Assert.Equal([2], gentle.StillRunning.Select(process => process.ProcessId));
        Assert.Equal(["close 1", "close 2"], control.Log);

        control.Log.Clear();
        var forced = await closer.CloseAsync([Process(2)], new ProcessCloseConsent(true, ForceAfterTimeout: true), TestContext.Current.CancellationToken);

        Assert.Equal(["close 2", "kill 2"], control.Log);
        Assert.Empty(forced.StillRunning);
    }

    [Fact]
    public void Detection_registers_only_executables_under_the_install_location()
    {
        var files = new FakeFiles();
        files.Directory(@"C:\Program Files\App");
        files.File(@"C:\Program Files\App\app.exe");
        files.File(@"C:\Program Files\App\shell.dll");
        files.Directory(@"C:\Program Files\App\bin");
        files.File(@"C:\Program Files\App\bin\helper.exe");
        files.Directory(@"C:\Program Files\App\link", reparse: true);
        files.File(@"C:\Program Files\App\link\other.exe");
        var holders = new FakeHolders([new RunningProcess(7, "App", null), new RunningProcess(7, "App", null)]);

        var processes = new RestartManagerProcessDetector(files, holders).Detect(Registry(quiet: null));

        Assert.Equal([@"C:\Program Files\App\app.exe", @"C:\Program Files\App\bin\helper.exe"], holders.Queried);
        Assert.Equal([7], processes.Select(process => process.ProcessId));

        var none = new FakeHolders([]);
        Assert.Empty(new RestartManagerProcessDetector(files, none).Detect(Registry(quiet: null) with { InstallLocation = null }));
        Assert.Null(none.Queried);
    }

    private static RunningProcess Process(int id) => new(id, "app" + id, null);

    private static InstalledProgram Msi(ProgramScope scope) => new()
    {
        Id = "prog:msi",
        DisplayName = "Msi App",
        Scope = scope,
        Source = ProgramSource.Registry,
        MsiProductCode = ProductCode,
        UninstallString = "MsiExec.exe /X" + ProductCode
    };

    private static InstalledProgram WinGet(ProgramScope scope) => new()
    {
        Id = "prog:winget",
        DisplayName = "WinGet App",
        Scope = scope,
        Source = ProgramSource.WinGet,
        WinGetPackageId = "Vendor.App"
    };

    private static InstalledProgram Registry(string? quiet, string? uninstall = null) => new()
    {
        Id = "prog:app",
        DisplayName = "App",
        Scope = ProgramScope.Machine,
        Source = ProgramSource.Registry,
        InstallLocation = @"C:\Program Files\App",
        QuietUninstallString = quiet,
        UninstallString = uninstall ?? $"\"{Unins}\""
    };

    private sealed class Machine
    {
        public FakeFolders Folders { get; } = new();

        public FakeFiles Files { get; } = new();

        public FakeRunner Runner { get; } = new();

        public FakeWinGet WinGet { get; } = new();

        public bool WithWinGet { get; init; } = true;

        public bool Elevated { get; set; }

        public Machine()
        {
            Files.File(Unins);
            Files.File(@"C:\Windows\System32\cmd.exe");
            Files.File(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
            Files.File(@"C:\Windows\System32\rundll32.exe");
            Files.File(@"C:\Temp\evil.exe");
        }

        public OfficialUninstallStep Select(InstalledProgram program) => OfficialStrategySelector.Select(program, Folders, Files, []);

        public WindowsOfficialUninstaller Uninstaller() =>
            new(WithWinGet ? WinGet : null, Runner, Folders, Files, () => Elevated, TimeSpan.FromMinutes(1));
    }

    private sealed class FakeFolders : IKnownFolderResolver
    {
        public string? Resolve(KnownFolder folder) => folder switch
        {
            KnownFolder.Windows => @"C:\Windows",
            KnownFolder.ProgramFiles => @"C:\Program Files",
            KnownFolder.ProgramFilesX86 => @"C:\Program Files (x86)",
            _ => null
        };
    }

    private sealed class FakeFiles : IFileSystemProbe
    {
        private readonly Dictionary<string, FileSystemEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public void File(string path) => _entries[path] = new FileSystemEntry(path, FileSystemEntryKind.File, 1, false, null);

        public void Directory(string path, bool reparse = false) =>
            _entries[path] = new FileSystemEntry(path, FileSystemEntryKind.Directory, 0, reparse, null);

        public FileSystemEntry? GetEntry(string path) => _entries.GetValueOrDefault(path);

        public IReadOnlyList<FileSystemEntry> ListChildren(string directoryPath) =>
            _entries.TryGetValue(directoryPath, out var directory) && directory.IsReparsePoint
                ? []
                : _entries.Values.Where(entry => string.Equals(Path.GetDirectoryName(entry.Path), directoryPath, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private sealed record RunnerCall(string Executable, IReadOnlyList<string> Arguments);

    private sealed class FakeRunner : IUninstallProcessRunner
    {
        public UninstallProcessResult Result { get; set; } = new(true, 0);

        public List<RunnerCall> Calls { get; } = [];

        public Task<UninstallProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            Calls.Add(new RunnerCall(executablePath, arguments));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeWinGet : IPackageUninstallProvider
    {
        public PackageOperationStatus Result { get; set; } = PackageOperationStatus.Succeeded;

        public string Code { get; set; } = "winget.success";

        public List<PackageUninstallRequest> Requests { get; } = [];

        public string ProviderId => PackageProviderIds.WinGet;

        public Task<PackageOperationResult> UninstallAsync(PackageUninstallRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new PackageOperationResult(Result, request.ApplicationId, request.Package, 0, Code));
        }

        public Task<PackageProviderAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PackageResolutionResult> ResolveAsync(ProviderPackageReference package, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PackageOperationResult> InstallAsync(PackageInstallRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeProcessControl : IProcessControl
    {
        public HashSet<int> Exits { get; init; } = [];

        public List<string> Log { get; } = [];

        private readonly HashSet<int> _killed = [];

        public bool RequestClose(int processId)
        {
            Log.Add("close " + processId);
            return true;
        }

        public bool HasExited(int processId) => Exits.Contains(processId) || _killed.Contains(processId);

        public void Kill(int processId)
        {
            Log.Add("kill " + processId);
            _killed.Add(processId);
        }
    }

    private sealed class FakeHolders(IReadOnlyList<RunningProcess> holders) : IFileHolderQuery
    {
        public IReadOnlyList<string>? Queried { get; private set; }

        public IReadOnlyList<RunningProcess>? FindHolders(IReadOnlyList<string> filePaths)
        {
            Queried = filePaths;
            return holders;
        }
    }
}
