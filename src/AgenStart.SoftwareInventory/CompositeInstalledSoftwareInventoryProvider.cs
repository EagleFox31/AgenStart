namespace AgenStart.SoftwareInventory;

public sealed class CompositeInstalledSoftwareInventoryProvider : IInstalledSoftwareInventoryProvider
{
    private readonly IReadOnlyList<IInstalledSoftwareCollector> _collectors;
    private readonly TimeProvider _timeProvider;

    public CompositeInstalledSoftwareInventoryProvider(
        IEnumerable<IInstalledSoftwareCollector> collectors,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(collectors);

        _collectors = collectors.ToArray();
        if (_collectors.Count == 0)
        {
            throw new ArgumentException(
                "At least one installed-software collector is required.",
                nameof(collectors));
        }

        if (_collectors.Any(static collector => collector is null))
        {
            throw new ArgumentException(
                "Installed-software collectors cannot contain null values.",
                nameof(collectors));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<InstalledSoftwareSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var records = new HashSet<InstalledSoftwareRecord>();
        var statuses = new Dictionary<string, InventorySourceStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var collector in _collectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await collector
                .CollectAsync(cancellationToken)
                .ConfigureAwait(false);

            ArgumentNullException.ThrowIfNull(result);

            foreach (var record in result.Records)
            {
                records.Add(record);
            }

            foreach (var status in result.Sources)
            {
                if (string.IsNullOrWhiteSpace(status.SourceId))
                {
                    throw new InvalidOperationException(
                        "Installed-software collectors must return a non-empty source ID.");
                }

                statuses[status.SourceId.Trim()] = status;
            }
        }

        return new InstalledSoftwareSnapshot(
            records
                .OrderBy(record => record.SourceKind)
                .ThenBy(record => record.SourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            statuses.Values
                .OrderBy(status => status.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _timeProvider.GetUtcNow());
    }
}
