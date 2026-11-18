using System.Text;
using System.Text.Json;
using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

// Golden plans of anonymised machine snapshots, planned with a frozen copy of the rules.
// Regenerate deliberately with AGENSTART_UPDATE_GOLDEN=1 and review the diff.
public sealed class UninstallPlanGoldenTests
{
    private const string UpdateVariable = "AGENSTART_UPDATE_GOLDEN";

    [Fact]
    public void GamePlanMatchesTheGolden()
    {
        var (machine, program, others) = PlanScenarios.Game();
        AssertMatchesGolden("game-plan.golden.json", machine.Plan(program, others));
    }

    [Fact]
    public void SuitePlanMatchesTheGolden()
    {
        var (machine, program, others) = PlanScenarios.Suite();
        AssertMatchesGolden("suite-plan.golden.json", machine.Plan(program, others));
    }

    [Fact]
    public void HostilePlanMatchesTheGolden()
    {
        var (machine, program, others) = PlanScenarios.Hostile();
        AssertMatchesGolden("hostile-plan.golden.json", machine.Plan(program, others));
    }

    private static void AssertMatchesGolden(string goldenFileName, UninstallPlan plan)
    {
        var actual = UninstallPlanJsonWriter.Write(plan, UninstallSelection.Default(plan));
        var goldenPath = Path.Combine(TestRules.GoldenDirectory(), goldenFileName);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(
            File.Exists(goldenPath),
            $"Golden file {goldenFileName} is missing. Generate it with {UpdateVariable}=1 and review it before committing.");

        Assert.Equal(Normalize(File.ReadAllText(goldenPath)), Normalize(actual));
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}

public sealed class UninstallPlanJsonWriterTests
{
    [Fact]
    public void WritesTheSelectionAndMarksThePlanAsNotExecuted()
    {
        var (machine, program, others) = PlanScenarios.Game();
        var plan = machine.Plan(program, others);
        var protectedItem = plan.ProtectedUserData[0];
        var selection = UninstallSelection.Default(plan).Append(protectedItem.ItemId).ToHashSet(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(UninstallPlanJsonWriter.Write(plan, selection, new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(1))));
        var root = document.RootElement;

        Assert.Equal(UninstallPlanJsonWriter.DocumentKind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("executed").GetBoolean());
        Assert.Equal("2026-11-18T09:00:00.0000000+00:00", root.GetProperty("exportedAt").GetString());
        var written = root.GetProperty("protectedUserData").EnumerateArray().Single(item => item.GetProperty("itemId").GetString() == protectedItem.ItemId);
        Assert.True(written.GetProperty("selected").GetBoolean());
        Assert.False(written.GetProperty("defaultSelected").GetBoolean());
        Assert.Equal("userData", written.GetProperty("dataClass").GetString());
        Assert.Equal(selection.Count, root.GetProperty("selection").GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public void FlagsASelectionAboveTheVolumeThresholds()
    {
        var (machine, program, others) = PlanScenarios.Game();
        var plan = machine.Plan(program, others);

        var summary = UninstallSelection.Summarize(plan, UninstallSelection.Default(plan));

        Assert.True(summary.TotalBytes > plan.Thresholds.ConfirmAboveBytes);
        Assert.True(summary.ExceedsVolumeThresholds);
        Assert.False(UninstallSelection.Summarize(plan, new HashSet<string>()).ExceedsVolumeThresholds);
    }
}
