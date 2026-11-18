using System.Globalization;

namespace AgenStart.Uninstall;

/// <summary>
/// Builds the installed-program list from uninstall registry entries: filter, map, de-duplicate.
/// Pure: the registry is read through <see cref="IUninstallRegistrySource"/>.
/// </summary>
public sealed class RegistryInstalledProgramInventory : IInstalledProgramInventory
{
    public const string PartialReadCode = "registry.partial-read";

    private readonly IUninstallRegistrySource _source;

    public RegistryInstalledProgramInventory(IUninstallRegistrySource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public Task<InstalledProgramInventoryResult> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Build(_source.ReadAll(cancellationToken)));
    }

    public static InstalledProgramInventoryResult Build(IReadOnlyList<UninstallRegistryProbeResult> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPrograms = new HashSet<string>(StringComparer.Ordinal);
        var programs = new List<InstalledProgram>();
        var diagnostics = new List<InventoryDiagnostic>();

        // Probes arrive in precedence order (HKLM 64, HKLM 32, HKCU 64, HKCU 32): the first sighting wins.
        foreach (var probe in probes)
        {
            if (probe.HadErrors || probe.Entries.Any(entry => entry.HasUnreadableValues))
            {
                diagnostics.Add(new InventoryDiagnostic(
                    PartialReadCode,
                    $"Some uninstall entries under {Describe(probe.Location)} could not be read as the current user."));
            }

            foreach (var entry in probe.Entries)
            {
                // HKCU is shared by both registry views: the same physical key is seen twice.
                if (!UninstallEntryFilter.IsListed(entry) || !seenKeys.Add(entry.KeyPath))
                {
                    continue;
                }

                var program = Map(entry);
                if (seenPrograms.Add(DuplicateKey(program)))
                {
                    programs.Add(program);
                }
            }
        }

        var status = diagnostics.Count == 0
            ? InstalledProgramInventoryStatus.Complete
            : InstalledProgramInventoryStatus.Partial;

        return new InstalledProgramInventoryResult(
            programs
                .OrderBy(program => program.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(program => program.Version, StringComparer.OrdinalIgnoreCase)
                .ThenBy(program => program.Id, StringComparer.Ordinal)
                .ToArray(),
            status,
            diagnostics);
    }

    public static InstalledProgram Map(UninstallRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var detection = InstallerKindDetector.Detect(entry);
        return new InstalledProgram
        {
            Id = InstalledProgramId.Create(ProgramSource.Registry, entry.KeyPath),
            DisplayName = entry.DisplayName?.Trim() ?? string.Empty,
            Publisher = entry.Publisher,
            Version = entry.DisplayVersion,
            Scope = entry.Location.Scope,
            Source = ProgramSource.Registry,
            RegistryKeyPath = entry.KeyPath,
            InstallLocation = entry.InstallLocation,
            UninstallString = entry.UninstallString,
            QuietUninstallString = entry.QuietUninstallString,
            MsiProductCode = detection.MsiProductCode,
            InstallerKind = detection.Kind,
            EstimatedSizeBytes = entry.EstimatedSizeKilobytes is > 0 and var kilobytes ? kilobytes * 1024 : null,
            InstallDate = ParseInstallDate(entry.InstallDate)
        };
    }

    /// <summary>
    /// Two entries are the same installation when name and version match and they share an MSI
    /// product code, an uninstall command or an install folder. Without any of these, entries stay apart.
    /// </summary>
    private static string DuplicateKey(InstalledProgram program)
    {
        var identity = program.MsiProductCode
            ?? Fold(program.UninstallString)
            ?? Fold(program.InstallLocation?.TrimEnd('\\', '/'))
            ?? $"key:{program.RegistryKeyPath?.ToUpperInvariant()}";

        return string.Join(
            '\n',
            ProgramNameNormalizer.Normalize(program.DisplayName),
            program.Version?.Trim().ToLowerInvariant() ?? string.Empty,
            identity);
    }

    private static string? Fold(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"').ToLowerInvariant();

    private static DateOnly? ParseInstallDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string Describe(UninstallRegistryLocation location)
    {
        var hive = location.Scope switch
        {
            ProgramScope.Machine => "HKEY_LOCAL_MACHINE",
            ProgramScope.User => "HKEY_CURRENT_USER",
            _ => "an unknown hive"
        };
        var view = location.View switch
        {
            UninstallRegistryView.Registry64 => "64-bit view",
            UninstallRegistryView.Registry32 => "32-bit view",
            _ => "unknown view"
        };
        return $"{hive} ({view})";
    }
}
