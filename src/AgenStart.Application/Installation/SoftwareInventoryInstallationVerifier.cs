using AgenStart.SoftwareInventory;

namespace AgenStart.Application.Installation;

public sealed class SoftwareInventoryInstallationVerifier : IInstallationVerifier
{
    private readonly IInstalledSoftwareInventoryProvider _inventoryProvider;
    private readonly IReadOnlyDictionary<string, SoftwareDetectionTarget> _targets;
    private readonly SoftwareStateResolver _resolver;

    public SoftwareInventoryInstallationVerifier(
        IInstalledSoftwareInventoryProvider inventoryProvider,
        IEnumerable<SoftwareDetectionTarget> targets,
        SoftwareStateResolver? resolver = null)
    {
        _inventoryProvider = inventoryProvider ?? throw new ArgumentNullException(nameof(inventoryProvider));
        ArgumentNullException.ThrowIfNull(targets);

        var targetMap = new Dictionary<string, SoftwareDetectionTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (!targetMap.TryAdd(target.ApplicationId, target))
            {
                throw new ArgumentException(
                    $"Software detection target {target.ApplicationId} was registered more than once.",
                    nameof(targets));
            }
        }

        _targets = targetMap;
        _resolver = resolver ?? new SoftwareStateResolver();
    }

    public async Task<InstallationVerificationResult> VerifyAsync(
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("An application id is required.", nameof(applicationId));
        }

        if (!_targets.TryGetValue(applicationId, out var target))
        {
            return new InstallationVerificationResult(
                InstallationVerificationStatus.Unknown,
                DiagnosticCode: "verification.target-missing",
                Message: $"No software detection target exists for {applicationId}.");
        }

        try
        {
            var snapshot = await _inventoryProvider
                .CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            var state = _resolver.Resolve([target], snapshot).Applications.Single();

            return state.State switch
            {
                SoftwarePresenceState.Installed => new InstallationVerificationResult(
                    InstallationVerificationStatus.Verified,
                    state.InstalledVersion,
                    Message: $"{applicationId} is present in the normalized software inventory."),
                SoftwarePresenceState.Missing => new InstallationVerificationResult(
                    InstallationVerificationStatus.NotInstalled,
                    DiagnosticCode: "verification.not-installed",
                    Message: $"{applicationId} was not detected after inventory refresh."),
                _ => new InstallationVerificationResult(
                    InstallationVerificationStatus.Unknown,
                    DiagnosticCode: state.Diagnostics.FirstOrDefault()?.Code ?? "verification.inventory-unknown",
                    Message: state.Diagnostics.FirstOrDefault()?.Message ??
                        $"Installed state for {applicationId} could not be determined safely.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new InstallationVerificationResult(
                InstallationVerificationStatus.Unknown,
                DiagnosticCode: "verification.inventory-failed",
                Message: $"Installed-state verification failed: {exception.GetType().Name}.");
        }
    }
}
