using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AgenStart.Uninstall;

/// <summary>The read ports the planner looks through. None of them can change the machine.</summary>
public sealed record UninstallReadPorts(
    IKnownFolderResolver Folders,
    IFileSystemProbe Files,
    IRegistryProbe Registry,
    IShortcutResolver Shortcuts,
    IProcessInspector Processes);

/// <summary>
/// Builds the uninstall plan of one program: the official step (described only) and the leftover
/// candidates that survive the safety policy, each scored and explained. Pure over its read ports:
/// the same machine state and rules always give the same plan, byte for byte.
/// </summary>
public sealed class UninstallPlanner
{
    public const string RulesUnavailable = "rules.unavailable";
    public const string NameTooGeneric = "rules.name-too-generic";
    public const string SizePartial = "size.partial";

    /// <summary>The size walk stops after this many entries and marks the size as partial.</summary>
    public const int MaximumWalkEntries = 20_000;

    private const int ShortcutScanDepth = 3;

    private readonly UninstallReadPorts _ports;
    private readonly string? _agenStartDataDirectory;

    public UninstallPlanner(UninstallReadPorts ports, string? agenStartDataDirectory)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _agenStartDataDirectory = agenStartDataDirectory;
    }

    public UninstallPlan Plan(InstalledProgram program, IReadOnlyList<InstalledProgram> allPrograms, UninstallRuleSet? rules)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(allPrograms);

        var diagnostics = new List<PlanDiagnostic>();
        var official = OfficialStrategySelector.Select(program, _ports.Folders, _ports.Files, diagnostics);
        var policy = PathSafetyPolicy.Create(_ports.Folders, rules, _agenStartDataDirectory);
        var context = new PlanContext(program, allPrograms.Where(other => other.Id != program.Id).ToArray(), policy)
        {
            CurrentProfile = WindowsPath.Normalize(_ports.Folders.Resolve(KnownFolder.UserProfile))
        };
        var processes = _ports.Processes.ListProcesses();

        var items = new List<PlanItem>();
        var skipped = new List<SkippedCandidate>();
        if (rules is null)
        {
            diagnostics.Add(new PlanDiagnostic(RulesUnavailable, "No valid rule set: only the official uninstall is shown."));
        }
        else
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in Candidates(context, rules, diagnostics))
            {
                var key = UninstallTokens.Kind(candidate.Kind) + "|" + candidate.Target;
                if (seen.TryGetValue(key, out var existing))
                {
                    if (candidate.DataClass == DataClass.UserData && existing >= 0)
                    {
                        items[existing] = Protect(items[existing], "rule-user-data");
                    }

                    continue;
                }

                var item = Evaluate(candidate, context, processes, out var skip);
                if (item is null)
                {
                    seen[key] = -1;
                    skipped.Add(skip!);
                    continue;
                }

                seen[key] = items.Count;
                items.Add(item);
            }
        }

        // A folder that holds protected user data is protected too: removing it would remove them.
        var userData = items.Where(item => item.DataClass == DataClass.UserData).Select(item => item.Target).ToArray();
        items = items
            .Select(item => item.DataClass != DataClass.UserData && item.Kind == LeftoverKind.Directory
                && userData.Any(path => WindowsPath.IsStrictlyUnder(path, item.Target))
                    ? Protect(item, "contains-user-data")
                    : item)
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Any(item => item.SizeIsPartial))
        {
            diagnostics.Add(new PlanDiagnostic(SizePartial, $"Some sizes stop after {MaximumWalkEntries} entries."));
        }

        var leftovers = items.Where(item => item.DataClass != DataClass.UserData).ToArray();
        var protectedData = items.Where(item => item.DataClass == DataClass.UserData).ToArray();
        var fileTargets = items.Where(item => item.Kind is LeftoverKind.Directory or LeftoverKind.File).Select(item => item.Target)
            .Concat(context.InstallLocation is null ? [] : [context.InstallLocation])
            .ToArray();
        var running = processes
            .Where(process => WindowsPath.Normalize(process.ImagePath) is { } image && fileTargets.Any(target => WindowsPath.IsSameOrUnder(image, target)))
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToArray();

        var state = Hash(string.Join('\n', new[]
        {
            program.Id, program.DisplayName, program.Version ?? string.Empty, program.InstallLocation ?? string.Empty,
            program.UninstallString ?? string.Empty, program.QuietUninstallString ?? string.Empty,
            program.MsiProductCode ?? string.Empty, program.WinGetPackageId ?? string.Empty
        }.Concat(items.Select(item => item.Fingerprint)).Concat(skipped.Select(skip => skip.Reason + "|" + skip.Target))));
        var planId = "plan:" + Hash($"{program.Id}|{state}|{rules?.RulesVersion}");

        return new UninstallPlan(
            planId,
            program.Id,
            program.DisplayName,
            state,
            rules?.RulesVersion,
            official,
            leftovers,
            protectedData,
            skipped,
            running,
            rules?.VolumeThresholds ?? VolumeThresholds.Default,
            diagnostics);
    }

    private IEnumerable<Candidate> Candidates(PlanContext context, UninstallRuleSet rules, List<PlanDiagnostic> diagnostics)
    {
        foreach (var rule in rules.ProductRules.Where(rule => Matches(rule.Match, context.Program)))
        {
            foreach (var leftover in rule.Leftovers)
            {
                var target = RuleTemplate.ResolveExplicit(leftover.Path, _ports.Folders, context.Program.InstallLocation);
                if (target is not null && Exists(leftover.Kind, target))
                {
                    yield return new Candidate(rule.Id, leftover.Kind, target, leftover.DataClass, leftover.Confidence, IsInstallLocation: false);
                }
            }
        }

        foreach (var rule in rules.GenericRules)
        {
            var needsPublisher = rule.RelativePath?.Contains(RuleTemplate.Publisher, StringComparison.Ordinal) == true;
            var needsProduct = rule.RelativePath?.Contains(RuleTemplate.Product, StringComparison.Ordinal) == true;
            if ((needsPublisher && context.PublisherKeys.Count == 0) || (needsProduct && context.ProductKeys.Count == 0))
            {
                diagnostics.Add(new PlanDiagnostic(NameTooGeneric, $"Rule {rule.Id} skipped: the program name or publisher is too generic to match."));
                continue;
            }

            foreach (var root in rule.Roots)
            {
                foreach (var target in Expand(rule, root, context))
                {
                    var isInstall = context.InstallLocation is not null && WindowsPath.AreEqual(target, context.InstallLocation);
                    yield return new Candidate(rule.Id, rule.Kind, target, rule.DataClass, rule.Confidence, isInstall);
                }
            }
        }
    }

    private IEnumerable<string> Expand(GenericLeftoverRule rule, string root, PlanContext context)
    {
        if (rule.Kind == LeftoverKind.RegistryKey)
        {
            return ExpandRegistry(rule, root, context);
        }

        if (root == RuleTemplate.InstallLocation && context.InstallLocation is null)
        {
            return [];
        }

        var basePath = RuleTemplate.ResolveExplicit(root, _ports.Folders, context.Program.InstallLocation);
        if (basePath is null)
        {
            return [];
        }

        if (rule.Kind == LeftoverKind.Shortcut)
        {
            return ExpandShortcuts(basePath, context);
        }

        IEnumerable<string> current = [basePath];
        foreach (var segment in (rule.RelativePath ?? string.Empty).Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = segment switch
            {
                RuleTemplate.Publisher => current.SelectMany(path => MatchingChildren(path, context.PublisherKeys, publisher: true)),
                RuleTemplate.Product => current.SelectMany(path => MatchingChildren(path, context.ProductKeys, publisher: false)),
                _ => current.Select(path => WindowsPath.Combine(path, segment))
            };
        }

        return current.Where(path => Exists(rule.Kind, path)).ToArray();
    }

    private IEnumerable<string> MatchingChildren(string directory, IReadOnlySet<string> keys, bool publisher) =>
        _ports.Files.ListChildren(directory)
            .Where(child => child.Kind == FileSystemEntryKind.Directory)
            .Select(child => WindowsPath.Normalize(child.Path))
            .OfType<string>()
            .Where(path => keys.Contains(publisher ? ProgramNameKeys.Publisher(WindowsPath.FileName(path)) : ProgramNameKeys.Product(WindowsPath.FileName(path))))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> ExpandRegistry(GenericLeftoverRule rule, string root, PlanContext context)
    {
        var basePath = RegistryPath.Normalize(root);
        if (basePath is null)
        {
            return [];
        }

        IEnumerable<string> current = [basePath];
        foreach (var segment in (rule.RelativePath ?? string.Empty).Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var names = segment switch
            {
                RuleTemplate.Publisher => context.PublisherRegistryNames,
                RuleTemplate.Product => context.ProductRegistryNames,
                _ => [segment]
            };
            current = current.SelectMany(path => names.Select(name => path + "\\" + name)).ToArray();
        }

        return current
            .Select(path => RegistryPath.Normalize(_ports.Registry.GetKey(path)?.Path))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<string> ExpandShortcuts(string root, PlanContext context)
    {
        // A shortcut is a leftover only when it points into the install location being removed,
        // and only when that location is itself a safe target (never a shared or system folder).
        if (context.InstallLocation is null || context.Policy.EvaluatePath(context.InstallLocation) != SafetyVerdict.Allowed)
        {
            return [];
        }

        var found = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            foreach (var child in _ports.Files.ListChildren(directory))
            {
                if (child.IsReparsePoint || WindowsPath.Normalize(child.Path) is not { } path)
                {
                    continue;
                }

                if (child.Kind == FileSystemEntryKind.Directory && depth + 1 < ShortcutScanDepth)
                {
                    pending.Push((path, depth + 1));
                }
                else if (child.Kind == FileSystemEntryKind.File
                    && string.Equals(WindowsPath.Extension(path), ".lnk", StringComparison.OrdinalIgnoreCase)
                    && WindowsPath.Normalize(_ports.Shortcuts.ResolveTarget(path)) is { } target
                    && WindowsPath.IsSameOrUnder(target, context.InstallLocation))
                {
                    found.Add(path);
                }
            }
        }

        return found.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private PlanItem? Evaluate(Candidate candidate, PlanContext context, IReadOnlyList<RunningProcess> processes, out SkippedCandidate? skipped)
    {
        skipped = null;
        var isRegistry = candidate.Kind == LeftoverKind.RegistryKey;
        var verdict = isRegistry ? context.Policy.EvaluateRegistryKey(candidate.Target) : context.Policy.EvaluatePath(candidate.Target);
        if (verdict != SafetyVerdict.Allowed)
        {
            skipped = new SkippedCandidate(candidate.RuleId, candidate.Kind, candidate.Target, verdict switch
            {
                SafetyVerdict.AncestorOfForbidden => SkipReasons.AncestorOfForbidden,
                SafetyVerdict.TooShallow => SkipReasons.TooShallow,
                SafetyVerdict.Invalid => SkipReasons.Invalid,
                _ => SkipReasons.Forbidden
            });
            return null;
        }

        if (!isRegistry && CrossesReparsePoint(candidate.Target))
        {
            skipped = new SkippedCandidate(candidate.RuleId, candidate.Kind, candidate.Target, SkipReasons.ReparsePoint);
            return null;
        }

        if (IsShared(candidate, context))
        {
            skipped = new SkippedCandidate(candidate.RuleId, candidate.Kind, candidate.Target, SkipReasons.Shared);
            return null;
        }

        var measure = isRegistry ? MeasureRegistry(candidate.Target) : Measure(candidate.Target);
        var segments = isRegistry ? RegistryPath.Segments(candidate.Target) : WindowsPath.Segments(candidate.Target);
        var signals = new ConfidenceSignals(
            candidate.Confidence,
            candidate.IsInstallLocation,
            context.PublisherKeys.Count > 0 && segments.Any(segment => context.PublisherKeys.Contains(ProgramNameKeys.Publisher(segment))),
            candidate.Kind is LeftoverKind.Directory or LeftoverKind.File && context.Policy.IsDirectlyUnderKnownFolder(candidate.Target),
            measure.HasExecutables && candidate.Kind != LeftoverKind.Shortcut,
            context.PublisherKeys.Count > 0 && context.Others.Any(other => context.PublisherKeys.Contains(ProgramNameKeys.Publisher(other.Publisher))),
            !isRegistry && processes.Any(process => WindowsPath.Normalize(process.ImagePath) is { } image && WindowsPath.IsSameOrUnder(image, candidate.Target)),
            context.Program.Source == ProgramSource.Msix);
        var confidence = ConfidenceScorer.Score(signals);

        var dataClass = candidate.DataClass;
        if (!isRegistry && (context.Policy.IsUserData(candidate.Target) || measure.HasCloudPlaceholders))
        {
            dataClass = DataClass.UserData;
        }

        var requiresElevation = isRegistry
            ? RegistryPath.Hive(candidate.Target) == RegistryPath.LocalMachine
            : context.CurrentProfile is null || !WindowsPath.IsStrictlyUnder(candidate.Target, context.CurrentProfile);
        var kindToken = UninstallTokens.Kind(candidate.Kind);
        return new PlanItem(
            "item:" + Hash($"{kindToken}|{candidate.Target.ToUpperInvariant()}"),
            candidate.RuleId,
            candidate.Kind,
            candidate.Target,
            measure.SizeBytes,
            measure.FileCount,
            measure.Partial,
            Hash($"{kindToken}|{candidate.Target.ToUpperInvariant()}|{measure.SizeBytes}|{measure.FileCount}|{measure.Stamp}"),
            confidence,
            dataClass,
            ConfidenceScorer.IsPreselected(confidence, dataClass),
            requiresElevation);
    }

    private bool IsShared(Candidate candidate, PlanContext context)
    {
        if (candidate.Kind == LeftoverKind.Shortcut)
        {
            return false;
        }

        if (candidate.Kind != LeftoverKind.RegistryKey)
        {
            foreach (var other in context.Others)
            {
                if (WindowsPath.Normalize(other.InstallLocation) is { } location
                    && (WindowsPath.IsSameOrUnder(candidate.Target, location) || WindowsPath.IsSameOrUnder(location, candidate.Target)))
                {
                    return true;
                }
            }
        }

        var leaf = ProgramNameKeys.Product(candidate.Kind == LeftoverKind.RegistryKey
            ? RegistryPath.Segments(candidate.Target)[^1]
            : WindowsPath.FileName(candidate.Target));
        if (context.OtherProductKeys.Contains(leaf))
        {
            // Another installed program carries this name (side-by-side versions included).
            return true;
        }

        return candidate.Kind == LeftoverKind.Directory
            && _ports.Files.ListChildren(candidate.Target)
                .Any(child => context.OtherProductKeys.Contains(ProgramNameKeys.Product(WindowsPath.FileName(child.Path))));
    }

    private bool CrossesReparsePoint(string target)
    {
        for (var path = target; path is not null && !WindowsPath.IsRoot(path); path = WindowsPath.Parent(path))
        {
            if (_ports.Files.GetEntry(path) is { IsReparsePoint: true })
            {
                return true;
            }
        }

        return false;
    }

    private Measurement Measure(string target)
    {
        var entry = _ports.Files.GetEntry(target);
        if (entry is null)
        {
            return new Measurement(null, null, false, false, false, string.Empty);
        }

        if (entry.Kind == FileSystemEntryKind.File)
        {
            return new Measurement(entry.SizeBytes, 1, false, IsExecutable(entry.Path), entry.IsCloudPlaceholder, Stamp(entry));
        }

        long size = 0;
        var files = 0;
        var visited = 0;
        var executables = false;
        var placeholders = false;
        var latest = entry.LastWriteTimeUtc;
        var pending = new Stack<string>();
        pending.Push(target);
        while (pending.Count > 0)
        {
            foreach (var child in _ports.Files.ListChildren(pending.Pop()))
            {
                if (++visited > MaximumWalkEntries)
                {
                    return new Measurement(size, files, true, executables, placeholders, Stamp(latest));
                }

                if (child.LastWriteTimeUtc > latest)
                {
                    latest = child.LastWriteTimeUtc;
                }

                placeholders |= child.IsCloudPlaceholder;
                if (child.IsReparsePoint)
                {
                    continue;
                }

                if (child.Kind == FileSystemEntryKind.Directory)
                {
                    pending.Push(child.Path);
                }
                else
                {
                    size += child.SizeBytes;
                    files++;
                    executables |= IsExecutable(child.Path);
                }
            }
        }

        return new Measurement(size, files, false, executables, placeholders, Stamp(latest));
    }

    private Measurement MeasureRegistry(string target)
    {
        var key = _ports.Registry.GetKey(target);
        return new Measurement(null, null, false, false, false, key is null ? string.Empty : $"{key.SubKeyCount}/{key.ValueCount}");
    }

    private bool Exists(LeftoverKind kind, string target) => kind switch
    {
        LeftoverKind.RegistryKey => _ports.Registry.GetKey(target) is not null,
        LeftoverKind.Directory => _ports.Files.GetEntry(target) is { Kind: FileSystemEntryKind.Directory },
        _ => _ports.Files.GetEntry(target) is { Kind: FileSystemEntryKind.File }
    };

    private static bool Matches(ProductRuleMatch match, InstalledProgram program)
    {
        var name = ProgramNameKeys.Product(program.DisplayName);
        var publisher = ProgramNameKeys.Publisher(program.Publisher);
        return match.DisplayNames.Any(candidate => ProgramNameKeys.Product(candidate) == name)
            && (match.Publishers.Count == 0 || match.Publishers.Any(candidate => ProgramNameKeys.Publisher(candidate) == publisher));
    }

    private static PlanItem Protect(PlanItem item, string reason) => item with
    {
        DataClass = DataClass.UserData,
        DefaultSelected = false,
        Confidence = item.Confidence with { Reasons = [.. item.Confidence.Reasons, new ConfidenceReason(reason, 0)] }
    };

    private static bool IsExecutable(string path) =>
        WindowsPath.Extension(path).ToUpperInvariant() is ".EXE" or ".DLL";

    private static string Stamp(FileSystemEntry entry) => Stamp(entry.LastWriteTimeUtc);

    private static string Stamp(DateTimeOffset? value) =>
        value?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    internal static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32];

    private sealed record Candidate(string RuleId, LeftoverKind Kind, string Target, DataClass DataClass, int Confidence, bool IsInstallLocation);

    private sealed record Measurement(long? SizeBytes, int? FileCount, bool Partial, bool HasExecutables, bool HasCloudPlaceholders, string Stamp);

    private sealed class PlanContext
    {
        public PlanContext(InstalledProgram program, IReadOnlyList<InstalledProgram> others, PathSafetyPolicy policy)
        {
            Program = program;
            Others = others;
            Policy = policy;
            InstallLocation = WindowsPath.Normalize(program.InstallLocation) is { } location && !WindowsPath.IsRoot(location) ? location : null;
            var installLeaf = InstallLocation is null ? null : WindowsPath.FileName(InstallLocation);
            ProductKeys = new[] { ProgramNameKeys.Product(program.DisplayName), ProgramNameKeys.Product(installLeaf) }
                .Where(policy.IsDistinctiveName)
                .ToHashSet(StringComparer.Ordinal);
            PublisherKeys = new[] { ProgramNameKeys.Publisher(program.Publisher) }
                .Where(policy.IsDistinctiveName)
                .ToHashSet(StringComparer.Ordinal);
            OtherProductKeys = others.Select(other => ProgramNameKeys.Product(other.DisplayName))
                .Where(policy.IsDistinctiveName)
                .ToHashSet(StringComparer.Ordinal);

            ProductRegistryNames = new[] { program.DisplayName.Trim(), ProgramNameKeys.Product(program.DisplayName), installLeaf }
                .OfType<string>()
                .Where(name => policy.IsDistinctiveName(ProgramNameKeys.Product(name)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PublisherRegistryNames = new[] { program.Publisher?.Trim(), ProgramNameKeys.Publisher(program.Publisher) }
                .OfType<string>()
                .Where(name => policy.IsDistinctiveName(ProgramNameKeys.Publisher(name)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public InstalledProgram Program { get; }

        public IReadOnlyList<InstalledProgram> Others { get; }

        public PathSafetyPolicy Policy { get; }

        public string? InstallLocation { get; }

        public string? CurrentProfile { get; init; }

        public HashSet<string> ProductKeys { get; }

        public HashSet<string> PublisherKeys { get; }

        public HashSet<string> OtherProductKeys { get; }

        public IReadOnlyList<string> ProductRegistryNames { get; }

        public IReadOnlyList<string> PublisherRegistryNames { get; }
    }
}
