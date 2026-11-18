using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

/// <summary>Outcome of one journaled action (ADR-0007). Persisted through <see cref="UninstallJournalTokens"/> only.</summary>
public enum UninstallOutcome
{
    Unknown = 0,
    Removed = 1,
    Recycled = 2,
    ExportedAndRemoved = 3,
    NoOp = 4,
    AlreadyRemoved = 5,
    SkippedChangedSinceReview = 6,
    SkippedNotApproved = 7,
    SkippedRequiresPermanentDelete = 8,
    RejectedByPolicy = 9,
    Failed = 10,
    Cancelled = 11
}

public abstract record UninstallJournalRecord(string SessionId, DateTimeOffset TimestampUtc);

public sealed record SessionStartedRecord(
    string SessionId,
    DateTimeOffset TimestampUtc,
    string PlanId,
    string ProgramId,
    string DisplayName,
    string StateFingerprint,
    bool Simulation) : UninstallJournalRecord(SessionId, TimestampUtc);

/// <summary>Written and flushed before the action starts. The command is kept as program + argument list.</summary>
public sealed record IntentRecord(
    string SessionId,
    DateTimeOffset TimestampUtc,
    string ItemId,
    string Kind,
    string Target,
    string Fingerprint,
    string Strategy,
    string? Executable,
    IReadOnlyList<string> Arguments) : UninstallJournalRecord(SessionId, TimestampUtc);

public sealed record ResultRecord(
    string SessionId,
    DateTimeOffset TimestampUtc,
    string ItemId,
    UninstallOutcome Outcome,
    int? ExitCode,
    string? BackupPath,
    string Message) : UninstallJournalRecord(SessionId, TimestampUtc);

public sealed record SessionFinishedRecord(
    string SessionId,
    DateTimeOffset TimestampUtc,
    UninstallOutcome Outcome,
    IReadOnlyDictionary<UninstallOutcome, int> Counts,
    long? BytesFreed) : UninstallJournalRecord(SessionId, TimestampUtc);

/// <summary>Explicit two-way string mappings (LL-003): unknown tokens read back as <see cref="UninstallOutcome.Unknown"/>.</summary>
public static class UninstallJournalTokens
{
    public const string OfficialUninstallKind = "official-uninstall";

    private static readonly (UninstallOutcome Outcome, string Token)[] Outcomes =
    [
        (UninstallOutcome.Removed, "removed"),
        (UninstallOutcome.Recycled, "recycled"),
        (UninstallOutcome.ExportedAndRemoved, "exported-and-removed"),
        (UninstallOutcome.NoOp, "no-op"),
        (UninstallOutcome.AlreadyRemoved, "already-removed"),
        (UninstallOutcome.SkippedChangedSinceReview, "skipped-changed-since-review"),
        (UninstallOutcome.SkippedNotApproved, "skipped-not-approved"),
        (UninstallOutcome.SkippedRequiresPermanentDelete, "skipped-requires-permanent-delete"),
        (UninstallOutcome.RejectedByPolicy, "rejected-by-policy"),
        (UninstallOutcome.Failed, "failed"),
        (UninstallOutcome.Cancelled, "cancelled")
    ];

    public static IEnumerable<UninstallOutcome> AllOutcomes => Outcomes.Select(entry => entry.Outcome);

    public static string Outcome(UninstallOutcome outcome)
    {
        foreach (var entry in Outcomes)
        {
            if (entry.Outcome == outcome)
            {
                return entry.Token;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome cannot be written.");
    }

    public static UninstallOutcome ParseOutcome(string? token)
    {
        foreach (var entry in Outcomes)
        {
            if (string.Equals(entry.Token, token, StringComparison.Ordinal))
            {
                return entry.Outcome;
            }
        }

        return UninstallOutcome.Unknown;
    }
}

/// <summary>One JSON object per line, fields in a fixed order (ADR-0007, schema 1).</summary>
public static class UninstallJournalFormat
{
    public const int SchemaVersion = 1;

    private const string SessionStarted = "session-started";
    private const string Intent = "intent";
    private const string Result = "result";
    private const string SessionFinished = "session-finished";

    public static string Serialize(UninstallJournalRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("sessionId", record.SessionId);
            writer.WriteString("timestampUtc", Timestamp(record.TimestampUtc));
            switch (record)
            {
                case SessionStartedRecord started:
                    writer.WriteString("type", SessionStarted);
                    writer.WriteString("planId", started.PlanId);
                    writer.WriteString("programId", started.ProgramId);
                    writer.WriteString("displayName", started.DisplayName);
                    writer.WriteString("stateFingerprint", started.StateFingerprint);
                    writer.WriteBoolean("simulation", started.Simulation);
                    break;
                case IntentRecord intent:
                    writer.WriteString("type", Intent);
                    writer.WriteString("itemId", intent.ItemId);
                    writer.WriteString("kind", intent.Kind);
                    writer.WriteString("target", intent.Target);
                    writer.WriteString("fingerprint", intent.Fingerprint);
                    writer.WriteString("strategy", intent.Strategy);
                    writer.WriteString("executable", intent.Executable);
                    writer.WriteStartArray("arguments");
                    foreach (var argument in intent.Arguments)
                    {
                        writer.WriteStringValue(argument);
                    }

                    writer.WriteEndArray();
                    break;
                case ResultRecord result:
                    writer.WriteString("type", Result);
                    writer.WriteString("itemId", result.ItemId);
                    writer.WriteString("outcome", UninstallJournalTokens.Outcome(result.Outcome));
                    if (result.ExitCode is { } exitCode)
                    {
                        writer.WriteNumber("exitCode", exitCode);
                    }
                    else
                    {
                        writer.WriteNull("exitCode");
                    }

                    writer.WriteString("backupPath", result.BackupPath);
                    writer.WriteString("message", result.Message);
                    break;
                case SessionFinishedRecord finished:
                    writer.WriteString("type", SessionFinished);
                    writer.WriteString("outcome", UninstallJournalTokens.Outcome(finished.Outcome));
                    writer.WriteStartObject("counts");
                    foreach (var outcome in UninstallJournalTokens.AllOutcomes)
                    {
                        if (finished.Counts.TryGetValue(outcome, out var count) && count > 0)
                        {
                            writer.WriteNumber(UninstallJournalTokens.Outcome(outcome), count);
                        }
                    }

                    writer.WriteEndObject();
                    if (finished.BytesFreed is { } bytes)
                    {
                        writer.WriteNumber("bytesFreed", bytes);
                    }
                    else
                    {
                        writer.WriteNull("bytesFreed");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unsupported journal record {record.GetType().Name}.", nameof(record));
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads one line back. A truncated, foreign or unknown line gives null rather than an exception.</summary>
    public static UninstallJournalRecord? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != SchemaVersion)
            {
                return null;
            }

            var sessionId = Text(root, "sessionId") ?? string.Empty;
            var timestamp = DateTimeOffset.TryParse(Text(root, "timestampUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;
            return Text(root, "type") switch
            {
                SessionStarted => new SessionStartedRecord(
                    sessionId, timestamp, Text(root, "planId") ?? string.Empty, Text(root, "programId") ?? string.Empty,
                    Text(root, "displayName") ?? string.Empty, Text(root, "stateFingerprint") ?? string.Empty,
                    root.TryGetProperty("simulation", out var simulation) && simulation.ValueKind == JsonValueKind.True),
                Intent => new IntentRecord(
                    sessionId, timestamp, Text(root, "itemId") ?? string.Empty, Text(root, "kind") ?? string.Empty,
                    Text(root, "target") ?? string.Empty, Text(root, "fingerprint") ?? string.Empty, Text(root, "strategy") ?? string.Empty,
                    Text(root, "executable"),
                    root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Array
                        ? arguments.EnumerateArray().Select(argument => argument.GetString() ?? string.Empty).ToArray()
                        : []),
                Result => new ResultRecord(
                    sessionId, timestamp, Text(root, "itemId") ?? string.Empty, UninstallJournalTokens.ParseOutcome(Text(root, "outcome")),
                    root.TryGetProperty("exitCode", out var exitCode) && exitCode.ValueKind == JsonValueKind.Number ? exitCode.GetInt32() : null,
                    Text(root, "backupPath"), Text(root, "message") ?? string.Empty),
                SessionFinished => new SessionFinishedRecord(
                    sessionId, timestamp, UninstallJournalTokens.ParseOutcome(Text(root, "outcome")), Counts(root),
                    root.TryGetProperty("bytesFreed", out var bytes) && bytes.ValueKind == JsonValueKind.Number ? bytes.GetInt64() : null),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Dictionary<UninstallOutcome, int> Counts(JsonElement root)
    {
        var counts = new Dictionary<UninstallOutcome, int>();
        if (root.TryGetProperty("counts", out var element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number)
                {
                    var outcome = UninstallJournalTokens.ParseOutcome(property.Name);
                    counts[outcome] = counts.GetValueOrDefault(outcome) + property.Value.GetInt32();
                }
            }
        }

        return counts;
    }
}

/// <summary>Append-only session journal. Each record is on disk before the caller moves on.</summary>
public interface IUninstallJournal
{
    string PathFor(string sessionId);

    Task AppendAsync(UninstallJournalRecord record, CancellationToken cancellationToken = default);
}

/// <summary><c>&lt;dataDir&gt;\uninstall\journal\&lt;sessionId&gt;.jsonl</c> (ADR-0007).</summary>
public sealed partial class JsonlUninstallJournal : IUninstallJournal
{
    private readonly string _directory;

    public JsonlUninstallJournal(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Path.IsPathFullyQualified(dataDirectory))
        {
            throw new ArgumentException("The AgenStart data directory must be an absolute path.", nameof(dataDirectory));
        }

        _directory = Path.Combine(dataDirectory, "uninstall", "journal");
    }

    public string PathFor(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || !SessionIdPattern().IsMatch(sessionId))
        {
            throw new ArgumentException("A session id is letters, digits and dashes only.", nameof(sessionId));
        }

        return Path.Combine(_directory, sessionId + ".jsonl");
    }

    public async Task AppendAsync(UninstallJournalRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var path = PathFor(record.SessionId);
        var line = Encoding.UTF8.GetBytes(UninstallJournalFormat.Serialize(record) + "\n");
        Directory.CreateDirectory(_directory);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Every readable record of a journal; a torn last line (crash mid-write) is skipped.</summary>
    public static async Task<IReadOnlyList<UninstallJournalRecord>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines.Select(UninstallJournalFormat.Parse).OfType<UninstallJournalRecord>().ToArray();
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,96}$", RegexOptions.CultureInvariant)]
    private static partial Regex SessionIdPattern();
}
