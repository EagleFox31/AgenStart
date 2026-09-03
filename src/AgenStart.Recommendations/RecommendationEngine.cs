using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.SoftwareInventory;

namespace AgenStart.Recommendations;

public sealed class RecommendationEngine
{
    private const long BytesPerMiB = 1024L * 1024L;

    public RecommendationPlan Build(RecommendationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Machine);
        ArgumentNullException.ThrowIfNull(request.Software);
        ArgumentNullException.ThrowIfNull(request.Applications);

        var applications = BuildApplicationIndex(request.Applications);
        ValidateGraphReferences(applications);
        var software = BuildSoftwareIndex(request.Software.Applications);

        var decisions = new List<RecommendationDecision>();

        foreach (var application in applications.Values)
        {
            var profileRule = ResolveProfileRule(application, request.Profile);
            if (profileRule is null)
            {
                continue;
            }

            decisions.Add(Evaluate(
                application,
                profileRule,
                request.Profile,
                request.Machine,
                software));
        }

        ResolveInstalledConflicts(decisions, applications, software);
        ResolveRecommendationConflicts(decisions, applications);

        return new RecommendationPlan(
            request.Profile,
            decisions
                .OrderBy(decision => LevelRank(decision.Level))
                .ThenBy(decision => decision.ApplicationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(decision => decision.ApplicationId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static Dictionary<string, ApplicationDefinition> BuildApplicationIndex(
        IReadOnlyList<ApplicationDefinition> applications)
    {
        var index = new Dictionary<string, ApplicationDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var application in applications)
        {
            ArgumentNullException.ThrowIfNull(application);
            var id = RequireText(application.Id, "Application id");
            RequireText(application.Name, $"Application name for {id}");

            if (!index.TryAdd(id, application))
            {
                throw new InvalidOperationException(
                    $"Duplicate canonical application id: {id}.");
            }

            ValidateRequirements(application.Requirements.Minimum, id, "minimum");
            ValidateRequirements(application.Requirements.Recommended, id, "recommended");
        }

        return index;
    }

    private static Dictionary<string, DetectedApplicationState> BuildSoftwareIndex(
        IReadOnlyList<DetectedApplicationState> states)
    {
        var index = new Dictionary<string, DetectedApplicationState>(StringComparer.OrdinalIgnoreCase);

        foreach (var state in states)
        {
            ArgumentNullException.ThrowIfNull(state);
            var id = RequireText(state.ApplicationId, "Software-state application id");
            if (!index.TryAdd(id, state))
            {
                throw new InvalidOperationException(
                    $"Duplicate normalized software state for application: {id}.");
            }
        }

        return index;
    }

    private static void ValidateGraphReferences(
        IReadOnlyDictionary<string, ApplicationDefinition> applications)
    {
        foreach (var application in applications.Values)
        {
            foreach (var dependency in application.Dependencies)
            {
                var id = RequireText(dependency, $"Dependency id for {application.Id}");
                if (string.Equals(id, application.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Application {application.Id} cannot depend on itself.");
                }

                if (!applications.ContainsKey(id))
                {
                    throw new InvalidOperationException(
                        $"Application {application.Id} references unknown dependency {id}.");
                }
            }

            foreach (var conflict in application.Conflicts)
            {
                var id = RequireText(conflict, $"Conflict id for {application.Id}");
                if (string.Equals(id, application.Id, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Application {application.Id} cannot conflict with itself.");
                }

                if (!applications.ContainsKey(id))
                {
                    throw new InvalidOperationException(
                        $"Application {application.Id} references unknown conflict {id}.");
                }
            }
        }
    }

    private static ProfileRecommendation? ResolveProfileRule(
        ApplicationDefinition application,
        UserProfile profile)
    {
        var matches = application.Recommendations
            .Where(rule => rule.Profile == profile)
            .ToArray();

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Application {application.Id} contains duplicate recommendations for profile {profile}.");
        }

        if (matches.Length == 0)
        {
            return null;
        }

        var rule = matches[0];
        RequireText(rule.ReasonKey, $"Recommendation reason key for {application.Id}/{profile}");
        return rule;
    }

    private static RecommendationDecision Evaluate(
        ApplicationDefinition application,
        ProfileRecommendation profileRule,
        UserProfile profile,
        MachineSnapshot machine,
        IReadOnlyDictionary<string, DetectedApplicationState> software)
    {
        var reasons = new List<RecommendationReason>
        {
            new(
                $"profile.{profileRule.ReasonKey}",
                ProfileMessage(application.Name, profile, profileRule.Level))
        };

        if (software.TryGetValue(application.Id, out var installedState))
        {
            if (installedState.State == SoftwarePresenceState.Installed)
            {
                reasons.Add(new RecommendationReason(
                    "software.already-installed",
                    installedState.InstalledVersion is { Length: > 0 } version
                        ? $"{application.Name} is already installed (version {version})."
                        : $"{application.Name} is already installed."));

                if (application.Lifecycle != ApplicationLifecycleStatus.Active)
                {
                    reasons.Add(LifecycleReason(application));
                }

                return Decision(
                    application,
                    profile,
                    profileRule,
                    RecommendationDisposition.AlreadyInstalled,
                    false,
                    reasons);
            }

            if (installedState.State == SoftwarePresenceState.Unknown)
            {
                reasons.Add(new RecommendationReason(
                    "software.presence-unknown",
                    $"AgenStart cannot safely confirm whether {application.Name} is already installed, so it will not preselect another installation."));

                return Decision(
                    application,
                    profile,
                    profileRule,
                    RecommendationDisposition.InventoryUnknown,
                    false,
                    reasons);
            }
        }
        else
        {
            reasons.Add(new RecommendationReason(
                "software.state-missing",
                $"No normalized installed-software state is available for {application.Name}; AgenStart will not guess that it is missing."));

            return Decision(
                application,
                profile,
                profileRule,
                RecommendationDisposition.InventoryUnknown,
                false,
                reasons);
        }

        if (application.Lifecycle != ApplicationLifecycleStatus.Active)
        {
            reasons.Add(LifecycleReason(application));
            return Decision(
                application,
                profile,
                profileRule,
                RecommendationDisposition.Unavailable,
                false,
                reasons);
        }

        var compatibility = EvaluateCompatibility(application, machine);
        reasons.AddRange(compatibility.Reasons);

        if (compatibility.HasHardFailure)
        {
            return Decision(
                application,
                profile,
                profileRule,
                RecommendationDisposition.Incompatible,
                false,
                reasons);
        }

        if (compatibility.HasUnknown)
        {
            return Decision(
                application,
                profile,
                profileRule,
                RecommendationDisposition.CompatibilityUnknown,
                false,
                reasons);
        }

        AddRecommendedCapabilityAdvisories(application, machine, reasons);

        return Decision(
            application,
            profile,
            profileRule,
            RecommendationDisposition.Recommended,
            profileRule.Level != RecommendationLevel.Optional,
            reasons);
    }

    private static CompatibilityResult EvaluateCompatibility(
        ApplicationDefinition application,
        MachineSnapshot machine)
    {
        var reasons = new List<RecommendationReason>();
        var hardFailure = false;
        var unknown = false;

        var platformRule = application.PlatformSupport
            .Where(rule => rule.Platform == machine.Platform.Kind)
            .ToArray();

        if (machine.Platform.Kind == PlatformKind.Unknown)
        {
            unknown = true;
            reasons.Add(new RecommendationReason(
                "capability.platform-unknown",
                $"The operating-system platform is unknown, so compatibility for {application.Name} cannot be verified."));
        }
        else if (platformRule.Length == 0)
        {
            hardFailure = true;
            reasons.Add(new RecommendationReason(
                "capability.platform-unsupported",
                $"{application.Name} is not supported by the catalogue on {machine.Platform.Kind}."));
        }
        else
        {
            if (platformRule.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Application {application.Id} contains duplicate platform support rules for {machine.Platform.Kind}.");
            }

            var support = platformRule[0];
            if (support.Status != PlatformSupportStatus.Supported)
            {
                hardFailure = true;
                reasons.Add(new RecommendationReason(
                    "capability.platform-unsupported",
                    support.Status == PlatformSupportStatus.Planned
                        ? $"{application.Name} support for {machine.Platform.Kind} is planned but not currently available."
                        : $"{application.Name} is unsupported on {machine.Platform.Kind}."));
            }
            else
            {
                EvaluateArchitecture(
                    application,
                    machine.Platform.Architecture,
                    support.Architectures,
                    application.Requirements.Minimum.Architectures,
                    reasons,
                    ref hardFailure,
                    ref unknown);
            }
        }

        EvaluateRam(
            application,
            machine.Memory.TotalPhysicalBytes,
            application.Requirements.Minimum.MinRamMiB,
            reasons,
            ref hardFailure,
            ref unknown);

        EvaluateStorage(
            application,
            machine.SystemDrive?.AvailableBytes,
            application.Requirements.Minimum.MinFreeStorageMiB,
            reasons,
            ref hardFailure,
            ref unknown);

        if (application.Requirements.Minimum.GpuRequired)
        {
            switch (machine.Capabilities.Gpu)
            {
                case GpuCapabilityState.Available:
                    break;
                case GpuCapabilityState.Unavailable:
                    hardFailure = true;
                    reasons.Add(new RecommendationReason(
                        "capability.gpu-required",
                        $"{application.Name} requires a GPU, but this machine reports no usable GPU capability."));
                    break;
                default:
                    unknown = true;
                    reasons.Add(new RecommendationReason(
                        "capability.gpu-unknown",
                        $"{application.Name} requires a GPU, but GPU capability could not be verified."));
                    break;
            }
        }

        return new CompatibilityResult(hardFailure, unknown, reasons);
    }

    private static void EvaluateArchitecture(
        ApplicationDefinition application,
        MachineArchitecture architecture,
        IReadOnlyList<MachineArchitecture> platformArchitectures,
        IReadOnlyList<MachineArchitecture> minimumArchitectures,
        ICollection<RecommendationReason> reasons,
        ref bool hardFailure,
        ref bool unknown)
    {
        var allowed = platformArchitectures
            .Intersect(minimumArchitectures.Count > 0 ? minimumArchitectures : platformArchitectures)
            .Distinct()
            .ToArray();

        if (allowed.Length == 0)
        {
            hardFailure = true;
            reasons.Add(new RecommendationReason(
                "capability.architecture-no-valid-target",
                $"{application.Name} has no valid architecture for the current catalogue/platform rules."));
            return;
        }

        if (architecture == MachineArchitecture.Unknown)
        {
            unknown = true;
            reasons.Add(new RecommendationReason(
                "capability.architecture-unknown",
                $"The machine architecture is unknown, so {application.Name} compatibility cannot be verified."));
            return;
        }

        if (!allowed.Contains(architecture))
        {
            hardFailure = true;
            reasons.Add(new RecommendationReason(
                "capability.architecture-unsupported",
                $"{application.Name} does not support the detected {architecture} architecture."));
        }
    }

    private static void EvaluateRam(
        ApplicationDefinition application,
        ulong? totalBytes,
        long? requiredMiB,
        ICollection<RecommendationReason> reasons,
        ref bool hardFailure,
        ref bool unknown)
    {
        if (requiredMiB is null or <= 0)
        {
            return;
        }

        if (totalBytes is null)
        {
            unknown = true;
            reasons.Add(new RecommendationReason(
                "capability.ram-unknown",
                $"{application.Name} requires at least {requiredMiB} MiB of RAM, but total memory could not be verified."));
            return;
        }

        var availableMiB = totalBytes.Value / (ulong)BytesPerMiB;
        if (availableMiB < (ulong)requiredMiB.Value)
        {
            hardFailure = true;
            reasons.Add(new RecommendationReason(
                "capability.ram-insufficient",
                $"{application.Name} requires at least {requiredMiB} MiB of RAM; this machine reports {availableMiB} MiB."));
        }
    }

    private static void EvaluateStorage(
        ApplicationDefinition application,
        long? availableBytes,
        long? requiredMiB,
        ICollection<RecommendationReason> reasons,
        ref bool hardFailure,
        ref bool unknown)
    {
        if (requiredMiB is null or <= 0)
        {
            return;
        }

        if (availableBytes is null)
        {
            unknown = true;
            reasons.Add(new RecommendationReason(
                "capability.storage-unknown",
                $"{application.Name} requires at least {requiredMiB} MiB of free storage, but system-drive free space could not be verified."));
            return;
        }

        if (availableBytes.Value < 0)
        {
            unknown = true;
            reasons.Add(new RecommendationReason(
                "capability.storage-invalid",
                $"System-drive free space is invalid, so {application.Name} compatibility cannot be verified."));
            return;
        }

        var availableMiB = availableBytes.Value / BytesPerMiB;
        if (availableMiB < requiredMiB.Value)
        {
            hardFailure = true;
            reasons.Add(new RecommendationReason(
                "capability.storage-insufficient",
                $"{application.Name} requires at least {requiredMiB} MiB of free storage; the system drive reports {availableMiB} MiB."));
        }
    }

    private static void AddRecommendedCapabilityAdvisories(
        ApplicationDefinition application,
        MachineSnapshot machine,
        ICollection<RecommendationReason> reasons)
    {
        var recommended = application.Requirements.Recommended;

        if (recommended.MinRamMiB is > 0 && machine.Memory.TotalPhysicalBytes is { } totalBytes)
        {
            var totalMiB = totalBytes / (ulong)BytesPerMiB;
            if (totalMiB < (ulong)recommended.MinRamMiB.Value)
            {
                reasons.Add(new RecommendationReason(
                    "capability.ram-below-recommended",
                    $"{application.Name} meets its minimum RAM requirement, but {recommended.MinRamMiB} MiB or more is recommended."));
            }
        }

        if (recommended.MinFreeStorageMiB is > 0 && machine.SystemDrive?.AvailableBytes is >= 0 and var availableBytes)
        {
            var availableMiB = availableBytes / BytesPerMiB;
            if (availableMiB < recommended.MinFreeStorageMiB.Value)
            {
                reasons.Add(new RecommendationReason(
                    "capability.storage-below-recommended",
                    $"{application.Name} meets its minimum storage requirement, but {recommended.MinFreeStorageMiB} MiB or more free space is recommended."));
            }
        }
    }

    private static void ResolveInstalledConflicts(
        IList<RecommendationDecision> decisions,
        IReadOnlyDictionary<string, ApplicationDefinition> applications,
        IReadOnlyDictionary<string, DetectedApplicationState> software)
    {
        for (var index = 0; index < decisions.Count; index++)
        {
            var decision = decisions[index];
            if (decision.Disposition != RecommendationDisposition.Recommended)
            {
                continue;
            }

            var application = applications[decision.ApplicationId];
            var installedConflict = applications.Values
                .Where(other => !string.Equals(other.Id, application.Id, StringComparison.OrdinalIgnoreCase))
                .Where(other => ConflictsWith(application, other))
                .FirstOrDefault(other =>
                    software.TryGetValue(other.Id, out var state) &&
                    state.State == SoftwarePresenceState.Installed);

            if (installedConflict is null)
            {
                continue;
            }

            decisions[index] = decision with
            {
                Disposition = RecommendationDisposition.Conflict,
                SelectedByDefault = false,
                Reasons = AppendReason(
                    decision.Reasons,
                    new RecommendationReason(
                        "conflict.installed-application",
                        $"{decision.ApplicationName} conflicts with already-installed {installedConflict.Name}, so it will not be preselected."))
            };
        }
    }

    private static void ResolveRecommendationConflicts(
        IList<RecommendationDecision> decisions,
        IReadOnlyDictionary<string, ApplicationDefinition> applications)
    {
        var order = Enumerable.Range(0, decisions.Count)
            .Where(index => decisions[index].Disposition == RecommendationDisposition.Recommended)
            .OrderBy(index => LevelRank(decisions[index].Level))
            .ThenBy(index => decisions[index].ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var winnerPosition = 0; winnerPosition < order.Length; winnerPosition++)
        {
            var winnerIndex = order[winnerPosition];
            if (decisions[winnerIndex].Disposition != RecommendationDisposition.Recommended)
            {
                continue;
            }

            var winner = applications[decisions[winnerIndex].ApplicationId];

            for (var loserPosition = winnerPosition + 1; loserPosition < order.Length; loserPosition++)
            {
                var loserIndex = order[loserPosition];
                var loserDecision = decisions[loserIndex];
                if (loserDecision.Disposition != RecommendationDisposition.Recommended)
                {
                    continue;
                }

                var loser = applications[loserDecision.ApplicationId];
                if (!ConflictsWith(winner, loser))
                {
                    continue;
                }

                decisions[loserIndex] = loserDecision with
                {
                    Disposition = RecommendationDisposition.Conflict,
                    SelectedByDefault = false,
                    Reasons = AppendReason(
                        loserDecision.Reasons,
                        new RecommendationReason(
                            "conflict.recommendation",
                            $"{loserDecision.ApplicationName} conflicts with the higher-priority recommendation {decisions[winnerIndex].ApplicationName}."))
                };
            }
        }
    }

    private static bool ConflictsWith(
        ApplicationDefinition first,
        ApplicationDefinition second) =>
        first.Conflicts.Contains(second.Id, StringComparer.OrdinalIgnoreCase) ||
        second.Conflicts.Contains(first.Id, StringComparer.OrdinalIgnoreCase);

    private static RecommendationDecision Decision(
        ApplicationDefinition application,
        UserProfile profile,
        ProfileRecommendation profileRule,
        RecommendationDisposition disposition,
        bool selectedByDefault,
        IReadOnlyList<RecommendationReason> reasons) =>
        new(
            application.Id,
            application.Name,
            profile,
            profileRule.Level,
            profileRule.ReasonKey,
            disposition,
            selectedByDefault,
            reasons.ToArray());

    private static RecommendationReason LifecycleReason(ApplicationDefinition application) =>
        application.Lifecycle switch
        {
            ApplicationLifecycleStatus.Deprecated => new RecommendationReason(
                "catalogue.lifecycle-deprecated",
                $"{application.Name} is deprecated in the AgenStart catalogue and is not recommended for a new installation."),
            ApplicationLifecycleStatus.Blocked => new RecommendationReason(
                "catalogue.lifecycle-blocked",
                $"{application.Name} is blocked by the AgenStart catalogue and cannot be recommended for installation."),
            _ => throw new ArgumentOutOfRangeException(nameof(application))
        };

    private static string ProfileMessage(
        string applicationName,
        UserProfile profile,
        RecommendationLevel level) =>
        level switch
        {
            RecommendationLevel.Essential =>
                $"{applicationName} is essential for the {profile} profile.",
            RecommendationLevel.Recommended =>
                $"{applicationName} is recommended for the {profile} profile.",
            RecommendationLevel.Optional =>
                $"{applicationName} is an optional suggestion for the {profile} profile.",
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

    private static IReadOnlyList<RecommendationReason> AppendReason(
        IReadOnlyList<RecommendationReason> existing,
        RecommendationReason reason) =>
        [.. existing, reason];

    private static int LevelRank(RecommendationLevel level) =>
        level switch
        {
            RecommendationLevel.Essential => 0,
            RecommendationLevel.Recommended => 1,
            RecommendationLevel.Optional => 2,
            _ => int.MaxValue
        };

    private static void ValidateRequirements(
        CapabilityRequirements requirements,
        string applicationId,
        string label)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        if (requirements.MinRamMiB is < 0)
        {
            throw new InvalidOperationException(
                $"Application {applicationId} has a negative {label} RAM requirement.");
        }

        if (requirements.MinFreeStorageMiB is < 0)
        {
            throw new InvalidOperationException(
                $"Application {applicationId} has a negative {label} storage requirement.");
        }
    }

    private static string RequireText(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} cannot be empty.");
        }

        return value.Trim();
    }

    private sealed record CompatibilityResult(
        bool HasHardFailure,
        bool HasUnknown,
        IReadOnlyList<RecommendationReason> Reasons);
}
