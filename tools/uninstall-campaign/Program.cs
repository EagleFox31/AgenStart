using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgenStart.Application.Uninstallation;
using AgenStart.Platform.Windows.Uninstall;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.Uninstall;

// Disposable-machine campaign (docs/design/2026-11-18-uninstall-module.md, P3). For every program in
// the manifest, already installed by run-uninstall-campaign.ps1, it prepares the plan, runs the same
// journaled session as the Desktop app, then runs it again and expects AlreadyRemoved.
// It REALLY uninstalls software: see Refusal() for the guard.

const string OptIn = "--confirm-disposable-machine";

if (Refusal(args) is { } refusal)
{
    Console.Error.WriteLine($"Refused: {refusal}");
    Console.Error.WriteLine("Usage: AgenStart.UninstallCampaign --manifest <json> --rules <json> --data-dir <abs dir> --report <json> " + OptIn);
    return 2;
}

var manifestPath = Option(args, "--manifest");
var rulesPath = Option(args, "--rules");
var dataDirectory = Option(args, "--data-dir");
var reportPath = Option(args, "--report");
if (manifestPath is null || rulesPath is null || dataDirectory is null || reportPath is null || !Path.IsPathFullyQualified(dataDirectory))
{
    Console.Error.WriteLine("Missing --manifest, --rules, --report or an absolute --data-dir.");
    return 2;
}

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
json.Converters.Add(new JsonStringEnumConverter());
var manifest = JsonSerializer.Deserialize<CampaignManifest>(File.ReadAllText(manifestPath), json)
    ?? throw new InvalidDataException("The campaign manifest is empty.");
var rulesJson = File.ReadAllText(rulesPath);

var inventory = new WindowsInstalledProgramInventory();
var planner = new UninstallPlanService(WindowsUninstallReadPorts.Create(), () => UninstallRuleSetLoader.Load(rulesJson), dataDirectory);

// Same composition as OfficialUninstallRunner.CreateForWindows in the Desktop app (kept in sync by
// hand: the Desktop project is a WinExe this tool does not reference).
var folders = new WindowsKnownFolderResolver();
var files = new WindowsFileSystemProbe();
var uninstaller = new WindowsOfficialUninstaller(new WinGetProvider(), new UninstallProcessRunner(), folders, files);
var journal = new JsonlUninstallJournal(dataDirectory);
var detector = new RestartManagerProcessDetector(files, new RestartManagerFileHolderQuery());
var closer = new WindowsProcessCloser();
UninstallSession NewSession() => new(new InventoryUninstallVerifier(inventory), uninstaller, journal, detector, closer);

var results = new List<TargetResult>();
foreach (var target in manifest.Targets)
{
    results.Add(await RunTargetAsync(target));
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(results, json));
foreach (var result in results)
{
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")}  {result.Name}: {string.Join("; ", result.Notes)}");
}

return results.All(result => result.Passed) ? 0 : 1;

async Task<TargetResult> RunTargetAsync(CampaignTarget target)
{
    var notes = new List<string>();
    var before = await inventory.CaptureAsync();
    var pattern = new Regex(target.DisplayNamePattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    var matches = before.Programs.Where(program => pattern.IsMatch(program.DisplayName)).ToArray();
    if (matches.Length != 1)
    {
        notes.Add($"expected one installed program matching '{target.DisplayNamePattern}', found {matches.Length}");
        return new TargetResult(target.Name, false, null, null, null, notes);
    }

    var program = matches[0];
    var passed = true;
    if (!string.Equals(program.Scope.ToString(), target.Scope, StringComparison.OrdinalIgnoreCase))
    {
        notes.Add($"scope {program.Scope}, expected {target.Scope}");
        passed = false;
    }

    var plan = await planner.CreatePlanAsync(program, before.Programs);
    if (!target.ExpectedStrategies.Contains(plan.Official.Strategy))
    {
        notes.Add($"strategy {plan.Official.Strategy}, expected one of {string.Join("/", target.ExpectedStrategies)}");
        passed = false;
    }

    // Running apps are never closed by the campaign: the person's consent is part of the product flow.
    var request = new UninstallRequest(plan, new UninstallConsent(Confirmed: true, ProcessCloseConsent.None));
    var first = await NewSession().RunAsync(request);
    notes.Add($"first: {first.Outcome} exit={first.ExitCode} code={first.DiagnosticCode} reboot={first.RebootRequired}");
    if (first.Outcome != UninstallOutcome.Removed)
    {
        passed = false;
    }

    var second = await NewSession().RunAsync(request);
    notes.Add($"second: {second.Outcome}");
    if (second.Outcome != UninstallOutcome.AlreadyRemoved)
    {
        passed = false;
    }

    var after = await inventory.CaptureAsync();
    if (after.Programs.Any(candidate => pattern.IsMatch(candidate.DisplayName)))
    {
        notes.Add("still listed in the inventory");
        passed = false;
    }

    return new TargetResult(target.Name, passed, plan.Official.Strategy, first, second, notes);
}

static string? Refusal(string[] args)
{
    if (!OperatingSystem.IsWindows())
    {
        return "Windows only.";
    }

    if (!args.Contains(OptIn, StringComparer.Ordinal))
    {
        return $"this tool really uninstalls software; pass {OptIn} on a disposable machine only.";
    }

    if (Environment.GetEnvironmentVariable("AGENSTART_DISPOSABLE_MACHINE") != "1")
    {
        return "AGENSTART_DISPOSABLE_MACHINE=1 is not set.";
    }

    var sandbox = string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase);
    var hostedRunner = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
        && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") == "github-hosted";
    return sandbox || hostedRunner
        ? null
        : "not inside Windows Sandbox (WDAGUtilityAccount) nor on a GitHub-hosted runner.";
}

static string? Option(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

internal sealed record CampaignManifest(IReadOnlyList<CampaignTarget> Targets);

internal sealed record CampaignTarget(
    string Name,
    string WinGetId,
    string Scope,
    string? InstallerType,
    string DisplayNamePattern,
    IReadOnlyList<OfficialStrategy> ExpectedStrategies);

internal sealed record TargetResult(
    string Name,
    bool Passed,
    OfficialStrategy? Strategy,
    UninstallReport? First,
    UninstallReport? Second,
    IReadOnlyList<string> Notes);
