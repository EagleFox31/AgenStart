using AgenStart.PackageManagement;

namespace AgenStart.Uninstall.Tests;

public sealed class UninstallExitCodeTests
{
    [Theory]
    [InlineData(0, PackageOperationStatus.Succeeded, "uninstall.succeeded", false)]
    [InlineData(3010, PackageOperationStatus.RebootRequired, "uninstall.reboot-required", false)]
    [InlineData(1641, PackageOperationStatus.RebootRequired, "uninstall.reboot-required", false)]
    [InlineData(1618, PackageOperationStatus.Failed, "uninstall.another-install-in-progress", true)]
    [InlineData(1602, PackageOperationStatus.CancelledByUser, "uninstall.cancelled-by-user", false)]
    [InlineData(1223, PackageOperationStatus.CancelledByUser, "uninstall.cancelled-by-user", false)]
    [InlineData(1605, PackageOperationStatus.NotFound, "uninstall.not-installed", false)]
    [InlineData(1, PackageOperationStatus.Failed, "uninstall.exit-code", false)]
    [InlineData(-2147024891, PackageOperationStatus.Failed, "uninstall.exit-code", false)]
    public void Exit_codes_map_to_a_status_per_ADR_0004(int exitCode, PackageOperationStatus status, string code, bool retryable)
    {
        var result = UninstallExitCodes.Normalize(exitCode);

        Assert.Equal(status, result.Status);
        Assert.Equal(code, result.DiagnosticCode);
        Assert.Equal(retryable, result.Retryable);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.True(result.Executed);
        Assert.Equal(exitCode is 0 or 3010 or 1641, result.IsSuccess);
        Assert.Equal(exitCode is 3010 or 1641, result.RebootRequired);
    }
}

public sealed class UninstallJournalTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 18, 9, 30, 0, 125, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "agenstart-journal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(UninstallOutcome.Removed, "removed")]
    [InlineData(UninstallOutcome.Recycled, "recycled")]
    [InlineData(UninstallOutcome.ExportedAndRemoved, "exported-and-removed")]
    [InlineData(UninstallOutcome.NoOp, "no-op")]
    [InlineData(UninstallOutcome.AlreadyRemoved, "already-removed")]
    [InlineData(UninstallOutcome.SkippedChangedSinceReview, "skipped-changed-since-review")]
    [InlineData(UninstallOutcome.SkippedNotApproved, "skipped-not-approved")]
    [InlineData(UninstallOutcome.SkippedRequiresPermanentDelete, "skipped-requires-permanent-delete")]
    [InlineData(UninstallOutcome.RejectedByPolicy, "rejected-by-policy")]
    [InlineData(UninstallOutcome.Failed, "failed")]
    [InlineData(UninstallOutcome.Cancelled, "cancelled")]
    public void Outcomes_map_both_ways(UninstallOutcome outcome, string token)
    {
        Assert.Equal(token, UninstallJournalTokens.Outcome(outcome));
        Assert.Equal(outcome, UninstallJournalTokens.ParseOutcome(token));
    }

    [Fact]
    public void Every_outcome_has_a_token_and_unknown_values_read_as_unknown()
    {
        foreach (var outcome in Enum.GetValues<UninstallOutcome>().Where(value => value != UninstallOutcome.Unknown))
        {
            Assert.NotEmpty(UninstallJournalTokens.Outcome(outcome));
        }

        Assert.Equal(UninstallOutcome.Unknown, UninstallJournalTokens.ParseOutcome("Removed"));
        Assert.Equal(UninstallOutcome.Unknown, UninstallJournalTokens.ParseOutcome("deleted-forever"));
        Assert.Equal(UninstallOutcome.Unknown, UninstallJournalTokens.ParseOutcome(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => UninstallJournalTokens.Outcome(UninstallOutcome.Unknown));
    }

    [Fact]
    public void Records_serialize_to_the_golden_lines()
    {
        UninstallJournalRecord[] records =
        [
            new SessionStartedRecord("s-1", At, "plan:abc", "prog:1", "Vendor App \"Pro\" é", "state:9", Simulation: true),
            new IntentRecord("s-1", At, "official", UninstallJournalTokens.OfficialUninstallKind, "prog:1", "state:9", "msi",
                @"C:\Windows\System32\msiexec.exe", ["/x", "{11111111-2222-3333-4444-555555555555}", "/qn", "/norestart"]),
            new ResultRecord("s-1", At, "official", UninstallOutcome.Removed, 3010, null, "Reboot required."),
            new SessionFinishedRecord("s-1", At, UninstallOutcome.Removed, new Dictionary<UninstallOutcome, int> { [UninstallOutcome.Removed] = 1, [UninstallOutcome.Failed] = 0 }, null)
        ];

        string[] golden =
        [
            """{"schemaVersion":1,"sessionId":"s-1","timestampUtc":"2026-11-18T09:30:00.125Z","type":"session-started","planId":"plan:abc","programId":"prog:1","displayName":"Vendor App \"Pro\" é","stateFingerprint":"state:9","simulation":true}""",
            """{"schemaVersion":1,"sessionId":"s-1","timestampUtc":"2026-11-18T09:30:00.125Z","type":"intent","itemId":"official","kind":"official-uninstall","target":"prog:1","fingerprint":"state:9","strategy":"msi","executable":"C:\\Windows\\System32\\msiexec.exe","arguments":["/x","{11111111-2222-3333-4444-555555555555}","/qn","/norestart"]}""",
            """{"schemaVersion":1,"sessionId":"s-1","timestampUtc":"2026-11-18T09:30:00.125Z","type":"result","itemId":"official","outcome":"removed","exitCode":3010,"backupPath":null,"message":"Reboot required."}""",
            """{"schemaVersion":1,"sessionId":"s-1","timestampUtc":"2026-11-18T09:30:00.125Z","type":"session-finished","outcome":"removed","counts":{"removed":1},"bytesFreed":null}"""
        ];

        Assert.Equal(golden, records.Select(UninstallJournalFormat.Serialize).ToArray());
        var parsed = golden.Select(UninstallJournalFormat.Parse).ToArray();
        Assert.Equal(records[0], parsed[0]);
        Assert.Equal(records[2], parsed[2]);
        var intent = Assert.IsType<IntentRecord>(parsed[1]);
        Assert.Equal(((IntentRecord)records[1]).Arguments, intent.Arguments);
        var finished = Assert.IsType<SessionFinishedRecord>(parsed[3]);
        Assert.Equal(1, finished.Counts[UninstallOutcome.Removed]);
    }

    [Fact]
    public async Task The_journal_is_append_only_under_the_data_directory_and_skips_a_torn_line()
    {
        var journal = new JsonlUninstallJournal(_directory);
        await journal.AppendAsync(new ResultRecord("s-2", At, "official", UninstallOutcome.AlreadyRemoved, null, null, "gone"), TestContext.Current.CancellationToken);
        await journal.AppendAsync(new ResultRecord("s-2", At, "official", UninstallOutcome.Failed, 1, null, "again"), TestContext.Current.CancellationToken);

        var path = journal.PathFor("s-2");
        Assert.Equal(Path.Combine(_directory, "uninstall", "journal", "s-2.jsonl"), path);
        await File.AppendAllTextAsync(path, """{"schemaVersion":1,"sessionId":"s-2","type":"res""", TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(path, "\n" + """{"schemaVersion":1,"sessionId":"s-2","type":"result","outcome":"vaporized"}""" + "\n", TestContext.Current.CancellationToken);

        var records = await JsonlUninstallJournal.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(3, records.Count);
        Assert.Equal(
            [UninstallOutcome.AlreadyRemoved, UninstallOutcome.Failed, UninstallOutcome.Unknown],
            records.Cast<ResultRecord>().Select(record => record.Outcome));
    }

    [Theory]
    [InlineData(@"..\escape")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("s 1")]
    public void Session_ids_cannot_leave_the_journal_directory(string sessionId)
    {
        Assert.Throws<ArgumentException>(() => new JsonlUninstallJournal(_directory).PathFor(sessionId));
    }

    [Fact]
    public void The_journal_needs_an_absolute_data_directory()
    {
        Assert.Throws<ArgumentException>(() => new JsonlUninstallJournal(@"relative\dir"));
    }
}
