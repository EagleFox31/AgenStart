using System.Runtime.CompilerServices;
using System.Text;
using AgenStart.Catalogue;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.SoftwareInventory;
using Xunit;

namespace AgenStart.Recommendations.Tests;

// Locks the complete recommendation output for a frozen catalogue so the
// usage-profile registry migration (ADR-0003) can prove it changes nothing.
// Regenerate deliberately with AGENSTART_UPDATE_GOLDEN=1 and review the diff.
public sealed class RecommendationGoldenTests
{
    private const string UpdateVariable = "AGENSTART_UPDATE_GOLDEN";

    // Canonical registry ids and order, independent of enum names and aliases.
    private static readonly (UserProfile Profile, string Id)[] ProfileIds =
    [
        (UserProfile.Personal, "personal"),
        (UserProfile.Development, "development"),
        (UserProfile.Business, "business"),
        (UserProfile.Creative, "creative"),
        (UserProfile.Learning, "learning"),
        (UserProfile.Gaming, "gaming")
    ];

    private static readonly UserProfile[] Selections =
    [
        UserProfile.Personal,
        UserProfile.Development,
        UserProfile.Business,
        UserProfile.Creative,
        UserProfile.Learning,
        UserProfile.Gaming,
        UserProfile.Personal | UserProfile.Development,
        UserProfile.Business | UserProfile.Learning,
        UserProfile.Creative | UserProfile.Gaming,
        UserProfile.Personal | UserProfile.Development | UserProfile.Business |
            UserProfile.Creative | UserProfile.Learning | UserProfile.Gaming
    ];

    [Fact]
    public void Capable_machine_with_partial_inventory_matches_golden() =>
        AssertMatchesGolden(
            "capable-machine.golden.txt",
            Machine(totalRamMiB: 16_384, freeStorageMiB: 64_000, gpu: GpuCapabilityState.Available),
            id => id switch
            {
                "git" => State(id, SoftwarePresenceState.Installed, "2.51.0"),
                "7zip" => State(id, SoftwarePresenceState.Installed, "24.09"),
                "docker-desktop" => State(id, SoftwarePresenceState.Unknown),
                _ => State(id, SoftwarePresenceState.Missing)
            });

    [Fact]
    public void Constrained_machine_without_gpu_matches_golden() =>
        AssertMatchesGolden(
            "constrained-machine.golden.txt",
            Machine(totalRamMiB: 4_096, freeStorageMiB: 8_000, gpu: GpuCapabilityState.Unavailable),
            id => State(id, SoftwarePresenceState.Missing));

    private static void AssertMatchesGolden(
        string goldenFileName,
        MachineSnapshot machine,
        Func<string, DetectedApplicationState> stateFor)
    {
        var applications = LoadFrozenCatalogue().Definitions;
        var software = new SoftwareDetectionResult(
            applications.Select(application => stateFor(application.Id)).ToArray(),
            0);
        var engine = new RecommendationEngine();

        var builder = new StringBuilder();
        foreach (var selection in Selections)
        {
            var plan = engine.Build(new RecommendationRequest(selection, machine, software, applications));
            AppendPlan(builder, selection, plan);
        }

        var actual = builder.ToString();
        var goldenPath = Path.Combine(GoldenDirectory(), goldenFileName);

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

    private static void AppendPlan(StringBuilder builder, UserProfile selection, RecommendationPlan plan)
    {
        builder.Append("## profiles: ").AppendLine(string.Join('+', ToIds(selection)));
        builder.Append("decisions: ").Append(plan.Decisions.Count)
            .Append(" | default selection: ").AppendLine(string.Join(", ", plan.DefaultSelection.Select(d => d.ApplicationId)));

        foreach (var decision in plan.Decisions)
        {
            builder
                .Append("- ").Append(decision.ApplicationId)
                .Append(" | ").Append(decision.ApplicationName)
                .Append(" | ").Append(decision.Level)
                .Append(" | ").Append(decision.Disposition)
                .Append(" | selected=").Append(decision.SelectedByDefault ? "yes" : "no")
                .Append(" | key=").Append(decision.ProfileReasonKey)
                .Append(" | matched=").AppendLine(string.Join('+', ToIds(decision.MatchedProfiles)));

            foreach (var reason in decision.Reasons)
            {
                builder.Append("    ").Append(reason.Code).Append(": ").AppendLine(reason.Message);
            }
        }

        builder.AppendLine();
    }

    private static IEnumerable<string> ToIds(UserProfile selection) =>
        ProfileIds.Where(entry => selection.HasFlag(entry.Profile)).Select(entry => entry.Id);

    private static IEnumerable<string> ToIds(IReadOnlyList<UserProfile>? profiles) =>
        profiles is null
            ? []
            : ProfileIds.Where(entry => profiles.Contains(entry.Profile)).Select(entry => entry.Id);

    private static SoftwareCatalogue LoadFrozenCatalogue()
    {
        using var stream = File.OpenRead(Path.Combine(GoldenDirectory(), "catalogue-0.2.0.frozen.json"));
        return new SoftwareCatalogueLoader().Load(stream);
    }

    private static string GoldenDirectory([CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, "Golden");

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static DetectedApplicationState State(
        string applicationId,
        SoftwarePresenceState state,
        string? version = null) =>
        new(
            applicationId,
            state,
            version,
            [],
            state == SoftwarePresenceState.Unknown
                ? [new SoftwareStateDiagnostic("fixture.unknown", "Fixture inventory is incomplete.")]
                : []);

    private static MachineSnapshot Machine(
        long totalRamMiB,
        long freeStorageMiB,
        GpuCapabilityState gpu) =>
        new(
            new PlatformSnapshot(
                PlatformKind.Windows,
                "Windows 11 Pro",
                "24H2",
                new Version(10, 0),
                "26100",
                null,
                MachineArchitecture.X64,
                MachineArchitecture.X64),
            new CpuSnapshot("Fixture CPU", MachineArchitecture.X64, 8),
            new MemorySnapshot(checked((ulong)totalRamMiB * 1024UL * 1024UL), null),
            gpu == GpuCapabilityState.Available
                ? [new GpuSnapshot("Fixture GPU", "Fixture Vendor")]
                : [],
            [new StorageSnapshot(
                "C:\\",
                StorageKind.Fixed,
                512L * 1024 * 1024 * 1024,
                freeStorageMiB * 1024L * 1024L,
                true)],
            new PackageManagerSnapshot(
                PackageManagerKind.WinGet,
                CapabilityState.Available,
                new Version(1, 12)),
            new CapabilitySnapshot(gpu),
            [],
            DateTimeOffset.UnixEpoch);
}
