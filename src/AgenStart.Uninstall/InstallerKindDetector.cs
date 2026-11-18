using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

public sealed record InstallerDetection(InstallerKind Kind, string? MsiProductCode);

/// <summary>
/// Recognises the installer technology from uninstall registry values only. It is deliberately
/// conservative: anything without a clear marker stays <see cref="InstallerKind.Unknown"/>.
/// </summary>
public static partial class InstallerKindDetector
{
    public static InstallerDetection Detect(UninstallRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var keyIsProductCode = TryCanonicalGuid(entry.KeyName, out var keyProductCode);
        var usesMsiexec = entry.UninstallString?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) == true;

        if (entry.WindowsInstaller || (keyIsProductCode && usesMsiexec))
        {
            var productCode = keyIsProductCode ? keyProductCode : ProductCodeFromCommand(entry.UninstallString);
            return new InstallerDetection(InstallerKind.Msi, productCode);
        }

        if (entry.InnoSetupAppPath is not null
            || entry.KeyName.EndsWith("_is1", StringComparison.OrdinalIgnoreCase))
        {
            return new InstallerDetection(InstallerKind.Inno, null);
        }

        if (entry.ValueNames.Any(name => name.StartsWith("NSIS:", StringComparison.OrdinalIgnoreCase)))
        {
            return new InstallerDetection(InstallerKind.Nsis, null);
        }

        return new InstallerDetection(InstallerKind.Unknown, null);
    }

    private static string? ProductCodeFromCommand(string? command)
    {
        if (command is null)
        {
            return null;
        }

        var match = BracedGuid().Match(command);
        return match.Success && TryCanonicalGuid(match.Value, out var productCode) ? productCode : null;
    }

    private static bool TryCanonicalGuid(string text, out string? canonical)
    {
        canonical = null;
        if (!Guid.TryParseExact(text.Trim(), "B", out var guid))
        {
            return false;
        }

        canonical = guid.ToString("B").ToUpperInvariant();
        return true;
    }

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex BracedGuid();
}
