using System.Text.Json;
using AgenStart.PackageManagement;
using AgenStart.Platform.Windows.WinGet;
using AgenStart.SoftwareInventory;

namespace AgenStart.Platform.Windows.SoftwareInventory;

public sealed class WinGetInstalledSoftwareCollector : IInstalledSoftwareCollector
{
    private static readonly string[] TrustedSources = ["winget", "msstore"];
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(90);

    private readonly IWinGetExecutableLocator _locator;
    private readonly IWinGetProcessRunner _runner;

    public WinGetInstalledSoftwareCollector()
        : this(new WinGetExecutableLocator(), new WinGetProcessRunner())
    {
    }

    public WinGetInstalledSoftwareCollector(
        IWinGetExecutableLocator locator,
        IWinGetProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<InstalledSoftwareCollectionResult> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var executable = _locator.Resolve();
        if (!executable.Found || string.IsNullOrWhiteSpace(executable.Path))
        {
            var statuses = TrustedSources
                .Select(source => new InventorySourceStatus(
                    SoftwareInventorySourceIds.ForPackageProvider(PackageProviderIds.WinGet, source),
                    InventorySourceState.Unavailable,
                    executable.DiagnosticCode ?? "winget.inventory-unavailable",
                    executable.Message ?? "WinGet is unavailable for installed-software inventory."))
                .ToArray();

            return new InstalledSoftwareCollectionResult([], statuses);
        }

        var records = new HashSet<InstalledSoftwareRecord>();
        var statuses = new List<InventorySourceStatus>(TrustedSources.Length);

        foreach (var source in TrustedSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExportSourceAsync(
                executable.Path,
                source,
                cancellationToken).ConfigureAwait(false);

            foreach (var record in result.Records)
            {
                records.Add(record);
            }

            statuses.Add(result.Status);
        }

        return new InstalledSoftwareCollectionResult(
            records
                .OrderBy(record => record.PackageSource, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            statuses);
    }

    private async Task<SourceExportResult> ExportSourceAsync(
        string executablePath,
        string source,
        CancellationToken cancellationToken)
    {
        var sourceId = SoftwareInventorySourceIds.ForPackageProvider(PackageProviderIds.WinGet, source);
        var directory = Path.Combine(Path.GetTempPath(), "AgenStart", "software-inventory");
        Directory.CreateDirectory(directory);

        var outputPath = Path.Combine(directory, $"{source}-{Guid.NewGuid():N}.json");
        var command = WinGetCommandBuilder.BuildExportInstalled(source, outputPath);

        try
        {
            var processResult = await _runner.RunAsync(
                executablePath,
                command.Arguments,
                ExportTimeout,
                cancellationToken).ConfigureAwait(false);

            var parsedRecords = TryReadExport(outputPath, source, out var parseFailed);

            if (parsedRecords.Count > 0 || File.Exists(outputPath))
            {
                var state = processResult.ExitCode == 0 && !parseFailed
                    ? InventorySourceState.Complete
                    : InventorySourceState.Partial;

                return new SourceExportResult(
                    parsedRecords,
                    new InventorySourceStatus(
                        sourceId,
                        state,
                        state == InventorySourceState.Partial ? "winget.export-partial" : null,
                        state == InventorySourceState.Partial
                            ? "WinGet produced only a partial or non-clean installed-package export for this trusted source."
                            : null));
            }

            var normalized = WinGetResultNormalizer.Normalize(processResult);
            var stateWithoutFile = normalized.Status switch
            {
                PackageOperationStatus.TimedOut => InventorySourceState.TimedOut,
                PackageOperationStatus.ProviderUnavailable => InventorySourceState.Unavailable,
                PackageOperationStatus.Cancelled or PackageOperationStatus.CancelledByUser => InventorySourceState.Partial,
                _ => InventorySourceState.Failed
            };

            return new SourceExportResult(
                [],
                new InventorySourceStatus(
                    sourceId,
                    stateWithoutFile,
                    $"winget.inventory.{normalized.DiagnosticCode}",
                    "WinGet could not produce an installed-package export for this trusted source."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SourceExportResult(
                [],
                new InventorySourceStatus(
                    sourceId,
                    InventorySourceState.Failed,
                    "winget.export-read-failed",
                    "AgenStart could not safely read the WinGet installed-package export."));
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    private static IReadOnlyList<InstalledSoftwareRecord> TryReadExport(
        string outputPath,
        string source,
        out bool parseFailed)
    {
        parseFailed = false;

        if (!File.Exists(outputPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(outputPath);
            return WinGetInstalledSoftwareExportParser.Parse(json, source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            parseFailed = true;
            return [];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup. No user file is ever targeted: the path is generated by AgenStart.
        }
    }

    private sealed record SourceExportResult(
        IReadOnlyList<InstalledSoftwareRecord> Records,
        InventorySourceStatus Status);
}
