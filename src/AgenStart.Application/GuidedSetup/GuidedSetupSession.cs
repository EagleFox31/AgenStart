using AgenStart.Application.Installation;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.PackageManagement;
using AgenStart.Recommendations;
using AgenStart.SoftwareInventory;

namespace AgenStart.Application.GuidedSetup;

public sealed class GuidedSetupSession : IDisposable
{
    private readonly RecommendationEngine _recommendationEngine;
    private readonly InstallationOrchestrator _installationOrchestrator;
    private readonly IReadOnlyList<GuidedApplicationCandidate> _candidates;
    private readonly SoftwareDetectionResult _software;
    private readonly Dictionary<string, GuidedRecommendationItem> _recommendations =
        new(StringComparer.OrdinalIgnoreCase);
    private InstallationSession? _installationSession;
    private bool _disposed;

    public GuidedSetupSession(
        MachineSnapshot machine,
        SoftwareDetectionResult software,
        IReadOnlyList<GuidedApplicationCandidate> candidates,
        RecommendationEngine recommendationEngine,
        InstallationOrchestrator installationOrchestrator)
    {
        Machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _software = software ?? throw new ArgumentNullException(nameof(software));
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _recommendationEngine = recommendationEngine ?? throw new ArgumentNullException(nameof(recommendationEngine));
        _installationOrchestrator = installationOrchestrator ?? throw new ArgumentNullException(nameof(installationOrchestrator));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in _candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(candidate.Definition);
            ArgumentNullException.ThrowIfNull(candidate.Package);
            if (!ids.Add(candidate.Definition.Id))
            {
                throw new ArgumentException(
                    $"Guided setup candidate {candidate.Definition.Id} is duplicated.",
                    nameof(candidates));
            }
        }
    }

    public GuidedSetupStep Step { get; private set; } = GuidedSetupStep.Welcome;
    public MachineSnapshot Machine { get; }
    public UserProfile? Profile { get; private set; }
    public bool InstallationConfirmed { get; private set; }
    public InstallationReport? InstallationReport { get; private set; }

    public IReadOnlyList<GuidedRecommendationItem> Recommendations =>
        _recommendations.Values
            .OrderBy(static item => item.Decision.Level)
            .ThenBy(static item => item.Decision.ApplicationName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public event EventHandler? Changed;
    public event EventHandler<InstallationProgressEvent>? InstallationProgressChanged;

    public void Continue()
    {
        ThrowIfDisposed();

        Step = Step switch
        {
            GuidedSetupStep.Welcome => GuidedSetupStep.MachineSummary,
            GuidedSetupStep.MachineSummary => GuidedSetupStep.ProfileSelection,
            GuidedSetupStep.Recommendations => GuidedSetupStep.Review,
            GuidedSetupStep.Review => GuidedSetupStep.Confirmation,
            _ => throw new InvalidOperationException(
                $"Continue is not valid from guided setup step {Step}.")
        };

        OnChanged();
    }

    public void SelectProfile(UserProfile profile)
    {
        ThrowIfDisposed();
        if (Step != GuidedSetupStep.ProfileSelection)
        {
            throw new InvalidOperationException("A profile can only be selected from the profile step.");
        }

        Profile = profile;
        var plan = _recommendationEngine.Build(
            new RecommendationRequest(
                profile,
                Machine,
                _software,
                _candidates.Select(static candidate => candidate.Definition).ToArray()));

        _recommendations.Clear();
        var candidateById = _candidates.ToDictionary(
            static candidate => candidate.Definition.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var decision in plan.Decisions)
        {
            var candidate = candidateById[decision.ApplicationId];
            var canSelect = decision.Disposition == RecommendationDisposition.Recommended;
            _recommendations.Add(
                decision.ApplicationId,
                new GuidedRecommendationItem(
                    decision,
                    candidate.Package,
                    canSelect && decision.SelectedByDefault,
                    canSelect));
        }

        Step = GuidedSetupStep.Recommendations;
        OnChanged();
    }

    public void SetSelected(string applicationId, bool selected)
    {
        ThrowIfDisposed();
        if (Step is not (GuidedSetupStep.Recommendations or GuidedSetupStep.Review))
        {
            throw new InvalidOperationException("Application selection can only change before confirmation.");
        }

        if (!_recommendations.TryGetValue(applicationId, out var item))
        {
            throw new KeyNotFoundException($"Recommendation {applicationId} was not found.");
        }

        if (!item.CanSelect && selected)
        {
            throw new InvalidOperationException(
                $"Application {applicationId} cannot be selected because its recommendation state is {item.Decision.Disposition}.");
        }

        _recommendations[applicationId] = item with { IsSelected = selected && item.CanSelect };
        OnChanged();
    }

    public async Task ConfirmAndInstallAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Step != GuidedSetupStep.Confirmation)
        {
            throw new InvalidOperationException("Installation can only start from the explicit confirmation step.");
        }

        InstallationConfirmed = true;
        var selections = Recommendations
            .Where(static item => item.IsSelected && item.CanSelect)
            .Select(static item => new InstallationSelection(
                item.ApplicationId,
                item.Package,
                Approved: true))
            .ToArray();

        _installationSession = _installationOrchestrator.CreateSession(selections);
        _installationSession.ProgressChanged += ForwardInstallationProgress;
        Step = GuidedSetupStep.Installation;
        OnChanged();

        InstallationReport = await _installationOrchestrator
            .RunAsync(_installationSession, cancellationToken)
            .ConfigureAwait(false);

        Step = GuidedSetupStep.Report;
        OnChanged();
    }

    public async Task RetryAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Step != GuidedSetupStep.Report || _installationSession is null)
        {
            throw new InvalidOperationException("Retry is only available from the final report of an active installation session.");
        }

        var item = InstallationReport?.Items.SingleOrDefault(candidate =>
            string.Equals(candidate.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.CanRetry)
        {
            throw new InvalidOperationException($"Application {applicationId} is not eligible for retry.");
        }

        Step = GuidedSetupStep.Installation;
        OnChanged();
        InstallationReport = await _installationOrchestrator
            .RetryAsync(_installationSession, applicationId, cancellationToken)
            .ConfigureAwait(false);
        Step = GuidedSetupStep.Report;
        OnChanged();
    }

    public void CancelInstallation()
    {
        ThrowIfDisposed();
        if (Step == GuidedSetupStep.Installation)
        {
            _installationSession?.Cancel();
        }
    }

    public GuidedSetupSnapshot Snapshot() =>
        new(
            Step,
            Machine,
            Profile,
            Recommendations,
            InstallationReport,
            InstallationConfirmed);

    private void ForwardInstallationProgress(object? sender, InstallationProgressEvent args)
    {
        InstallationProgressChanged?.Invoke(this, args);
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_installationSession is not null)
        {
            _installationSession.ProgressChanged -= ForwardInstallationProgress;
            _installationSession.Dispose();
        }
    }
}
