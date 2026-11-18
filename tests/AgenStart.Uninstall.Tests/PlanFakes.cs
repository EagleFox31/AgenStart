using System.Runtime.CompilerServices;
using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

/// <summary>Known folders of a fake machine. Every path is configurable, so redirected and localised folders are testable.</summary>
internal sealed class FakeKnownFolderResolver : IKnownFolderResolver
{
    private readonly Dictionary<KnownFolder, string> _folders;

    public FakeKnownFolderResolver(IDictionary<KnownFolder, string>? overrides = null, string user = "alex")
    {
        var profile = $@"C:\Users\{user}";
        _folders = new Dictionary<KnownFolder, string>
        {
            [KnownFolder.UserProfile] = profile,
            [KnownFolder.UserProfiles] = @"C:\Users",
            [KnownFolder.LocalAppData] = $@"{profile}\AppData\Local",
            [KnownFolder.RoamingAppData] = $@"{profile}\AppData\Roaming",
            [KnownFolder.LocalLow] = $@"{profile}\AppData\LocalLow",
            [KnownFolder.ProgramData] = @"C:\ProgramData",
            [KnownFolder.Documents] = $@"{profile}\Documents",
            [KnownFolder.Desktop] = $@"{profile}\Desktop",
            [KnownFolder.Downloads] = $@"{profile}\Downloads",
            [KnownFolder.SavedGames] = $@"{profile}\Saved Games",
            [KnownFolder.StartMenuPrograms] = $@"{profile}\AppData\Roaming\Microsoft\Windows\Start Menu\Programs",
            [KnownFolder.CommonStartMenuPrograms] = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
            [KnownFolder.PublicDesktop] = @"C:\Users\Public\Desktop",
            [KnownFolder.PublicDocuments] = @"C:\Users\Public\Documents",
            [KnownFolder.Temp] = $@"{profile}\AppData\Local\Temp",
            [KnownFolder.Windows] = @"C:\Windows",
            [KnownFolder.ProgramFiles] = @"C:\Program Files",
            [KnownFolder.ProgramFilesX86] = @"C:\Program Files (x86)",
            [KnownFolder.CommonFiles] = @"C:\Program Files\Common Files",
            [KnownFolder.CommonFilesX86] = @"C:\Program Files (x86)\Common Files"
        };

        foreach (var (folder, path) in overrides ?? new Dictionary<KnownFolder, string>())
        {
            _folders[folder] = path;
        }
    }

    public string? Resolve(KnownFolder folder) => _folders.TryGetValue(folder, out var path) ? path : null;

    public IEnumerable<string> All => _folders.Values;
}

/// <summary>An in-memory, case-insensitive Windows file tree. Parents are created implicitly.</summary>
internal sealed class InMemoryFileSystemProbe : IFileSystemProbe
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, FileSystemEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase);

    public int ListCalls { get; private set; }

    public InMemoryFileSystemProbe Directory(string path, bool reparse = false, bool placeholder = false)
    {
        Add(new FileSystemEntry(Normalized(path), FileSystemEntryKind.Directory, 0, reparse, Stamp, placeholder));
        return this;
    }

    public InMemoryFileSystemProbe File(string path, long size = 100, bool placeholder = false)
    {
        Add(new FileSystemEntry(Normalized(path), FileSystemEntryKind.File, size, false, Stamp, placeholder));
        return this;
    }

    public FileSystemEntry? GetEntry(string path) =>
        WindowsPath.Normalize(path) is { } key && _entries.TryGetValue(key, out var entry) ? entry : null;

    public IReadOnlyList<FileSystemEntry> ListChildren(string directoryPath)
    {
        ListCalls++;
        return WindowsPath.Normalize(directoryPath) is { } key && _children.TryGetValue(key, out var children)
            ? children.Select(child => _entries[child]).ToArray()
            : [];
    }

    private void Add(FileSystemEntry entry)
    {
        var parent = WindowsPath.Parent(entry.Path);
        if (parent is not null && !_entries.ContainsKey(parent))
        {
            Add(new FileSystemEntry(parent, FileSystemEntryKind.Directory, 0, false, Stamp));
        }

        if (_entries.ContainsKey(entry.Path))
        {
            _entries[entry.Path] = entry;
            return;
        }

        _entries[entry.Path] = entry;
        if (parent is not null)
        {
            if (!_children.TryGetValue(parent, out var list))
            {
                _children[parent] = list = [];
            }

            list.Add(entry.Path);
        }
    }

    private static string Normalized(string path) =>
        WindowsPath.Normalize(path) ?? throw new ArgumentException($"Not an absolute path: {path}", nameof(path));
}

internal sealed class FakeRegistryProbe : IRegistryProbe
{
    private readonly Dictionary<string, RegistryKeyEntry> _keys = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistryProbe Key(string path, int subKeys = 0, int values = 1)
    {
        var normalized = RegistryPath.Normalize(path) ?? throw new ArgumentException(path, nameof(path));
        _keys[normalized] = new RegistryKeyEntry(normalized, subKeys, values);
        return this;
    }

    public RegistryKeyEntry? GetKey(string keyPath) =>
        RegistryPath.Normalize(keyPath) is { } key && _keys.TryGetValue(key, out var entry) ? entry : null;
}

internal sealed class FakeShortcutResolver : IShortcutResolver
{
    private readonly Dictionary<string, string> _targets = new(StringComparer.OrdinalIgnoreCase);

    public FakeShortcutResolver Link(string shortcut, string target)
    {
        _targets[WindowsPath.Normalize(shortcut)!] = target;
        return this;
    }

    public string? ResolveTarget(string shortcutPath) =>
        WindowsPath.Normalize(shortcutPath) is { } key && _targets.TryGetValue(key, out var target) ? target : null;
}

internal sealed class FakeProcessInspector : IProcessInspector
{
    private readonly List<RunningProcess> _processes = [];

    public FakeProcessInspector Running(int id, string name, string? image)
    {
        _processes.Add(new RunningProcess(id, name, image));
        return this;
    }

    public IReadOnlyList<RunningProcess> ListProcesses() => _processes;
}

/// <summary>One fake machine: folders, files, registry, shortcuts and processes, all read-only from the planner's side.</summary>
internal sealed class FakeMachine
{
    public FakeKnownFolderResolver Folders { get; init; } = new();

    public InMemoryFileSystemProbe Files { get; } = new();

    public FakeRegistryProbe Registry { get; } = new();

    public FakeShortcutResolver Shortcuts { get; } = new();

    public FakeProcessInspector Processes { get; } = new();

    public UninstallReadPorts Ports => new(Folders, Files, Registry, Shortcuts, Processes);

    public UninstallPlan Plan(InstalledProgram program, IEnumerable<InstalledProgram>? others = null, UninstallRuleSet? rules = null, string? dataDirectory = null)
    {
        var all = new[] { program }.Concat(others ?? []).ToArray();
        return new UninstallPlanner(Ports, dataDirectory).Plan(program, all, rules ?? TestRules.Frozen);
    }
}

internal static class TestRules
{
    private static readonly Lazy<UninstallRuleSet> FrozenRules = new(() =>
    {
        using var stream = System.IO.File.OpenRead(Path.Combine(GoldenDirectory(), "uninstall-rules-2026.11.0.frozen.json"));
        return UninstallRuleSetLoader.Load(stream);
    });

    /// <summary>A frozen copy of the shipped rules, so golden plans change only when the planner does.</summary>
    public static UninstallRuleSet Frozen => FrozenRules.Value;

    public static string ShippedRulesPath() => Path.Combine(RepositoryRoot(), "docs", "product", "uninstall-rules.json");

    public static string ShippedSchemaPath() => Path.Combine(RepositoryRoot(), "docs", "product", "uninstall-rules.schema.json");

    public static string GoldenDirectory([CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "Golden");

    private static string RepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}

internal static class Programs
{
    public static InstalledProgram Create(
        string name,
        string? publisher = null,
        string? installLocation = null,
        string? uninstall = null,
        string? quiet = null,
        string? msi = null,
        string? winget = null,
        InstallerKind kind = InstallerKind.Unknown,
        ProgramScope scope = ProgramScope.Machine,
        ProgramSource source = ProgramSource.Registry,
        string? version = "1.0") => new()
        {
            Id = InstalledProgramId.Create(source, $@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{name}"),
            DisplayName = name,
            Publisher = publisher,
            Version = version,
            Scope = scope,
            Source = source,
            InstallLocation = installLocation,
            UninstallString = uninstall,
            QuietUninstallString = quiet,
            MsiProductCode = msi,
            WinGetPackageId = winget,
            InstallerKind = kind
        };
}
