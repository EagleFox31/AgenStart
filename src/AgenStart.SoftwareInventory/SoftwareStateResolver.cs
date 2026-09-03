using System.Text.RegularExpressions;

namespace AgenStart.SoftwareInventory;

public sealed partial class SoftwareStateResolver
{
    public SoftwareDetectionResult Resolve(
        IReadOnlyList<SoftwareDetectionTarget> targets,
        InstalledSoftwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(snapshot);

        ValidateTargets(targets);

        var sourceStatus = snapshot.Sources
            .GroupBy(status => Normalize(status.SourceId), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);

        var providerOwners = BuildProviderOwnerIndex(targets);
        var registryMatches = BuildRegistryMatches(targets, snapshot.Records);
        var providerMatches = BuildProviderMatches(targets, snapshot.Records, providerOwners);
        var usedEvidence = new HashSet<InstalledSoftwareRecord>();
        var applicationStates = new List<DetectedApplicationState>(targets.Count);

        foreach (var target in targets)
        {
            var diagnostics = new List<SoftwareStateDiagnostic>();
            var evidence = new List<InstalledSoftwareRecord>();

            if (providerMatches.AmbiguousTargets.Contains(target.ApplicationId))
            {
                diagnostics.Add(new SoftwareStateDiagnostic(
                    "inventory.ambiguous-provider-identity",
                    "A provider package identity maps to more than one catalogue application."));
            }
            else if (providerMatches.ByApplication.TryGetValue(target.ApplicationId, out var providerEvidence))
            {
                evidence.AddRange(providerEvidence);
            }

            if (registryMatches.AmbiguousTargets.Contains(target.ApplicationId))
            {
                diagnostics.Add(new SoftwareStateDiagnostic(
                    "inventory.ambiguous-registry-match",
                    "A registry entry matches more than one catalogue application and was not treated as confirmed."));
            }
            else if (registryMatches.ByApplication.TryGetValue(target.ApplicationId, out var registryEvidence))
            {
                evidence.AddRange(registryEvidence);
            }

            if (evidence.Count > 0)
            {
                foreach (var record in evidence)
                {
                    usedEvidence.Add(record);
                }

                var version = ResolveVersion(evidence, diagnostics);
                applicationStates.Add(new DetectedApplicationState(
                    target.ApplicationId,
                    SoftwarePresenceState.Installed,
                    version,
                    evidence,
                    diagnostics));
                continue;
            }

            if (diagnostics.Count > 0)
            {
                applicationStates.Add(new DetectedApplicationState(
                    target.ApplicationId,
                    SoftwarePresenceState.Unknown,
                    null,
                    [],
                    diagnostics));
                continue;
            }

            var presence = CanProveMissing(target, sourceStatus)
                ? SoftwarePresenceState.Missing
                : SoftwarePresenceState.Unknown;

            if (presence == SoftwarePresenceState.Unknown)
            {
                diagnostics.Add(new SoftwareStateDiagnostic(
                    "inventory.insufficient-evidence",
                    "The required inventory source did not complete, so absence cannot be confirmed."));
            }

            applicationStates.Add(new DetectedApplicationState(
                target.ApplicationId,
                presence,
                null,
                [],
                diagnostics));
        }

        return new SoftwareDetectionResult(
            applicationStates,
            snapshot.Records.Count(record => !usedEvidence.Contains(record)));
    }

    private static bool CanProveMissing(
        SoftwareDetectionTarget target,
        IReadOnlyDictionary<string, InventorySourceStatus> sourceStatus)
    {
        if (target.ProviderPackages.Count == 0)
        {
            return IsComplete(sourceStatus, SoftwareInventorySourceIds.WindowsRegistry);
        }

        var requiredProviderSources = target.ProviderPackages
            .Select(package => SoftwareInventorySourceIds.ForPackageProvider(package.ProviderId, package.Source))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return requiredProviderSources.Length > 0 &&
               requiredProviderSources.All(sourceId => IsComplete(sourceStatus, sourceId));
    }

    private static bool IsComplete(
        IReadOnlyDictionary<string, InventorySourceStatus> sourceStatus,
        string sourceId) =>
        sourceStatus.TryGetValue(Normalize(sourceId), out var status) && status.IsComplete;

    private static string? ResolveVersion(
        IReadOnlyList<InstalledSoftwareRecord> evidence,
        ICollection<SoftwareStateDiagnostic> diagnostics)
    {
        var versions = evidence
            .Select(record => record.Version?.Trim())
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        if (versions.Length == 1)
        {
            return versions[0];
        }

        if (versions.Length > 1)
        {
            diagnostics.Add(new SoftwareStateDiagnostic(
                "inventory.multiple-installed-versions",
                "Multiple installed versions were detected; the application is installed but no single version is reported."));
        }

        return null;
    }

    private static ProviderMatchIndex BuildProviderMatches(
        IReadOnlyList<SoftwareDetectionTarget> targets,
        IReadOnlyList<InstalledSoftwareRecord> records,
        IReadOnlyDictionary<string, IReadOnlyList<string>> providerOwners)
    {
        var byApplication = new Dictionary<string, List<InstalledSoftwareRecord>>(StringComparer.Ordinal);
        var ambiguousTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records.Where(record => record.SourceKind == InstalledSoftwareSourceKind.PackageProvider))
        {
            if (string.IsNullOrWhiteSpace(record.ProviderId) ||
                string.IsNullOrWhiteSpace(record.PackageId) ||
                string.IsNullOrWhiteSpace(record.PackageSource))
            {
                continue;
            }

            var key = ProviderKey(record.ProviderId, record.PackageSource, record.PackageId);
            if (!providerOwners.TryGetValue(key, out var owners) || owners.Count == 0)
            {
                continue;
            }

            if (owners.Count > 1)
            {
                foreach (var owner in owners)
                {
                    ambiguousTargets.Add(owner);
                }

                continue;
            }

            Add(byApplication, owners[0], record);
        }

        return new ProviderMatchIndex(byApplication, ambiguousTargets);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildProviderOwnerIndex(
        IReadOnlyList<SoftwareDetectionTarget> targets)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var target in targets)
        {
            foreach (var package in target.ProviderPackages)
            {
                var key = ProviderKey(package.ProviderId, package.Source, package.PackageId);
                if (!index.TryGetValue(key, out var owners))
                {
                    owners = [];
                    index[key] = owners;
                }

                if (!owners.Contains(target.ApplicationId, StringComparer.Ordinal))
                {
                    owners.Add(target.ApplicationId);
                }
            }
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static RegistryMatchIndex BuildRegistryMatches(
        IReadOnlyList<SoftwareDetectionTarget> targets,
        IReadOnlyList<InstalledSoftwareRecord> records)
    {
        var byApplication = new Dictionary<string, List<InstalledSoftwareRecord>>(StringComparer.Ordinal);
        var ambiguousTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records.Where(record => record.SourceKind == InstalledSoftwareSourceKind.Registry))
        {
            var owners = targets
                .Where(target => RegistryRecordMatchesTarget(record, target))
                .Select(target => target.ApplicationId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (owners.Length == 0)
            {
                continue;
            }

            if (owners.Length > 1)
            {
                foreach (var owner in owners)
                {
                    ambiguousTargets.Add(owner);
                }

                continue;
            }

            Add(byApplication, owners[0], record);
        }

        foreach (var ambiguousTarget in ambiguousTargets)
        {
            byApplication.Remove(ambiguousTarget);
        }

        return new RegistryMatchIndex(byApplication, ambiguousTargets);
    }

    private static bool RegistryRecordMatchesTarget(
        InstalledSoftwareRecord record,
        SoftwareDetectionTarget target)
    {
        var displayName = Normalize(record.DisplayName);
        if (displayName.Length == 0)
        {
            return false;
        }

        var explicitNames = target.RegistryDisplayNames
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (explicitNames.Contains(displayName))
        {
            return PublisherCompatible(record.Publisher, target.Publisher);
        }

        return displayName == Normalize(target.DisplayName) &&
               PublisherCompatible(record.Publisher, target.Publisher);
    }

    private static bool PublisherCompatible(string? observedPublisher, string expectedPublisher)
    {
        var expected = Normalize(expectedPublisher);
        var observed = Normalize(observedPublisher);

        if (expected.Length == 0)
        {
            return true;
        }

        return observed.Length > 0 && observed == expected;
    }

    private static string ProviderKey(string providerId, string source, string packageId) =>
        $"{Normalize(providerId)}\u001f{Normalize(source)}\u001f{Normalize(packageId)}";

    private static void Add(
        IDictionary<string, List<InstalledSoftwareRecord>> index,
        string applicationId,
        InstalledSoftwareRecord record)
    {
        if (!index.TryGetValue(applicationId, out var records))
        {
            records = [];
            index[applicationId] = records;
        }

        if (!records.Contains(record))
        {
            records.Add(record);
        }
    }

    private static void ValidateTargets(IReadOnlyList<SoftwareDetectionTarget> targets)
    {
        var duplicates = targets
            .GroupBy(target => target.ApplicationId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new ArgumentException(
                $"Software detection targets contain duplicate application IDs: {string.Join(", ", duplicates)}",
                nameof(targets));
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespacePattern()
            .Replace(value.Trim(), " ")
            .ToLowerInvariant();
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    private sealed record ProviderMatchIndex(
        IReadOnlyDictionary<string, List<InstalledSoftwareRecord>> ByApplication,
        IReadOnlySet<string> AmbiguousTargets);

    private sealed record RegistryMatchIndex(
        IReadOnlyDictionary<string, List<InstalledSoftwareRecord>> ByApplication,
        IReadOnlySet<string> AmbiguousTargets);
}
