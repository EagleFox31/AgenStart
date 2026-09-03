using System.Text.Json;
using AgenStart.PackageManagement;
using AgenStart.SoftwareInventory;

namespace AgenStart.Platform.Windows.SoftwareInventory;

public static class WinGetInstalledSoftwareExportParser
{
    public static IReadOnlyList<InstalledSoftwareRecord> Parse(
        string json,
        string expectedSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSource);

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32
        });

        if (!document.RootElement.TryGetProperty("Sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("WinGet export does not contain a Sources array.");
        }

        var records = new HashSet<InstalledSoftwareRecord>();
        var sourceId = SoftwareInventorySourceIds.ForPackageProvider(
            PackageProviderIds.WinGet,
            expectedSource);

        foreach (var source in sources.EnumerateArray())
        {
            if (!TryGetSourceName(source, out var sourceName) ||
                !string.Equals(sourceName, expectedSource, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!source.TryGetProperty("Packages", out var packages) ||
                packages.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var package in packages.EnumerateArray())
            {
                if (!package.TryGetProperty("PackageIdentifier", out var identifierElement) ||
                    identifierElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var packageId = identifierElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                var version = ReadOptionalString(package, "Version");
                var scope = ReadScope(package);

                records.Add(new InstalledSoftwareRecord(
                    InstalledSoftwareSourceKind.PackageProvider,
                    sourceId,
                    packageId,
                    Version: version,
                    Scope: scope,
                    ProviderId: PackageProviderIds.WinGet,
                    PackageId: packageId,
                    PackageSource: expectedSource));
            }
        }

        return records
            .OrderBy(record => record.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetSourceName(JsonElement source, out string? sourceName)
    {
        sourceName = null;

        if (!source.TryGetProperty("SourceDetails", out var details) ||
            details.ValueKind != JsonValueKind.Object ||
            !details.TryGetProperty("Name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        sourceName = nameElement.GetString()?.Trim();
        return !string.IsNullOrWhiteSpace(sourceName);
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static InstalledSoftwareScope ReadScope(JsonElement package) =>
        ReadOptionalString(package, "Scope")?.ToLowerInvariant() switch
        {
            "user" => InstalledSoftwareScope.User,
            "machine" => InstalledSoftwareScope.Machine,
            _ => InstalledSoftwareScope.Unknown
        };
}
