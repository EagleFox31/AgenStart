using System.Text;
using System.Text.Json;

namespace AgenStart.Uninstall;

/// <summary>What the current selection amounts to, and whether it needs the extra volume confirmation.</summary>
public sealed record PlanSelectionSummary(int ItemCount, long TotalBytes, int FileCount, bool ExceedsVolumeThresholds);

public static class UninstallSelection
{
    public static IReadOnlySet<string> Default(UninstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Leftovers.Concat(plan.ProtectedUserData)
            .Where(item => item.DefaultSelected)
            .Select(item => item.ItemId)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static PlanSelectionSummary Summarize(UninstallPlan plan, IReadOnlySet<string> selectedItemIds)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedItemIds);

        var selected = plan.Leftovers.Concat(plan.ProtectedUserData).Where(item => selectedItemIds.Contains(item.ItemId)).ToArray();
        var bytes = selected.Sum(item => item.SizeBytes ?? 0);
        var files = selected.Sum(item => item.FileCount ?? 0);
        return new PlanSelectionSummary(
            selected.Length,
            bytes,
            files,
            bytes > plan.Thresholds.ConfirmAboveBytes || files > plan.Thresholds.ConfirmAboveFileCount);
    }
}

/// <summary>
/// The exported plan (JSON, schema 1.0). Deterministic for a given plan and selection, so it can be
/// diffed and used as a golden file. Tokens come from explicit mappings, never enum names (LL-003).
/// </summary>
public static class UninstallPlanJsonWriter
{
    public const string DocumentKind = "agenstart.uninstall-plan";
    public const string SchemaVersion = "1.0";

    public static string Write(UninstallPlan plan, IReadOnlySet<string> selectedItemIds, DateTimeOffset? exportedAt = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedItemIds);

        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("kind", DocumentKind);
            json.WriteString("schemaVersion", SchemaVersion);
            if (exportedAt is not null)
            {
                json.WriteString("exportedAt", exportedAt.Value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }

            json.WriteString("planId", plan.PlanId);
            json.WriteString("programId", plan.ProgramId);
            json.WriteString("programName", plan.ProgramName);
            json.WriteString("stateFingerprint", plan.StateFingerprint);
            json.WriteString("rulesVersion", plan.RulesVersion);
            json.WriteBoolean("executed", false);

            json.WriteStartObject("official");
            json.WriteString("strategy", PlanTokens.Strategy(plan.Official.Strategy));
            json.WriteString("executable", plan.Official.Executable);
            json.WriteStartArray("arguments");
            foreach (var argument in plan.Official.Arguments)
            {
                json.WriteStringValue(argument);
            }

            json.WriteEndArray();
            json.WriteString("commandPreview", plan.Official.CommandPreview);
            json.WriteBoolean("silent", plan.Official.IsSilent);
            json.WriteBoolean("requiresElevation", plan.Official.RequiresElevation);
            json.WriteEndObject();

            WriteItems(json, "leftovers", plan.Leftovers, selectedItemIds);
            WriteItems(json, "protectedUserData", plan.ProtectedUserData, selectedItemIds);

            json.WriteStartArray("skipped");
            foreach (var skipped in plan.Skipped)
            {
                json.WriteStartObject();
                json.WriteString("ruleId", skipped.RuleId);
                json.WriteString("kind", UninstallTokens.Kind(skipped.Kind));
                json.WriteString("target", skipped.Target);
                json.WriteString("reason", skipped.Reason);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            json.WriteStartArray("processes");
            foreach (var process in plan.Processes)
            {
                json.WriteStartObject();
                json.WriteNumber("processId", process.ProcessId);
                json.WriteString("name", process.Name);
                json.WriteString("imagePath", process.ImagePath);
                json.WriteEndObject();
            }

            json.WriteEndArray();

            var summary = UninstallSelection.Summarize(plan, selectedItemIds);
            json.WriteStartObject("selection");
            json.WriteNumber("itemCount", summary.ItemCount);
            json.WriteNumber("totalBytes", summary.TotalBytes);
            json.WriteNumber("fileCount", summary.FileCount);
            json.WriteBoolean("exceedsVolumeThresholds", summary.ExceedsVolumeThresholds);
            json.WriteEndObject();

            json.WriteStartArray("diagnostics");
            foreach (var diagnostic in plan.Diagnostics)
            {
                json.WriteStartObject();
                json.WriteString("code", diagnostic.Code);
                json.WriteString("message", diagnostic.Message);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteItems(Utf8JsonWriter json, string name, IReadOnlyList<PlanItem> items, IReadOnlySet<string> selectedItemIds)
    {
        json.WriteStartArray(name);
        foreach (var item in items)
        {
            json.WriteStartObject();
            json.WriteString("itemId", item.ItemId);
            json.WriteString("ruleId", item.RuleId);
            json.WriteString("kind", UninstallTokens.Kind(item.Kind));
            json.WriteString("target", item.Target);
            if (item.SizeBytes is { } size)
            {
                json.WriteNumber("sizeBytes", size);
            }
            else
            {
                json.WriteNull("sizeBytes");
            }

            if (item.FileCount is { } count)
            {
                json.WriteNumber("fileCount", count);
            }
            else
            {
                json.WriteNull("fileCount");
            }

            json.WriteBoolean("sizeIsPartial", item.SizeIsPartial);
            json.WriteString("fingerprint", item.Fingerprint);
            json.WriteStartObject("confidence");
            json.WriteNumber("score", item.Confidence.Score);
            json.WriteString("level", PlanTokens.Level(item.Confidence.Level));
            json.WriteStartArray("reasons");
            foreach (var reason in item.Confidence.Reasons)
            {
                json.WriteStartObject();
                json.WriteString("code", reason.Code);
                json.WriteNumber("weight", reason.Weight);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteString("dataClass", UninstallTokens.DataClassToken(item.DataClass));
            json.WriteBoolean("defaultSelected", item.DefaultSelected);
            json.WriteBoolean("selected", selectedItemIds.Contains(item.ItemId));
            json.WriteBoolean("requiresElevation", item.RequiresElevation);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
