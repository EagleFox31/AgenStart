namespace AgenStart.Uninstall;

/// <summary>Read port the UI asks for a plan. Producing a plan reads the machine and changes nothing.</summary>
public interface IUninstallPlanSource
{
    Task<UninstallPlan> CreatePlanAsync(
        InstalledProgram program,
        IReadOnlyList<InstalledProgram> allPrograms,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads the rule file once (failing closed) and runs the planner off the UI thread. When the rule
/// file is invalid the plan still carries the official uninstall, with no leftover and a diagnostic.
/// </summary>
public sealed class UninstallPlanService : IUninstallPlanSource
{
    public const string RulesInvalid = "rules.invalid";

    private readonly UninstallPlanner _planner;
    private readonly Lazy<(UninstallRuleSet? Rules, string? Error)> _rules;

    public UninstallPlanService(UninstallReadPorts ports, Func<UninstallRuleSet> loadRules, string? agenStartDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(loadRules);
        _planner = new UninstallPlanner(ports, agenStartDataDirectory);
        _rules = new Lazy<(UninstallRuleSet?, string?)>(() =>
        {
            try
            {
                return (loadRules(), null);
            }
            catch (InvalidDataException exception)
            {
                return (null, exception.Message);
            }
        });
    }

    public Task<UninstallPlan> CreatePlanAsync(
        InstalledProgram program,
        IReadOnlyList<InstalledProgram> allPrograms,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(allPrograms);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (rules, error) = _rules.Value;
            var plan = _planner.Plan(program, allPrograms, rules);
            return error is null
                ? plan
                : plan with { Diagnostics = [new PlanDiagnostic(RulesInvalid, error), .. plan.Diagnostics] };
        }, cancellationToken);
    }
}
