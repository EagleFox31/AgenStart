using System.Security.Cryptography;
using System.Text;
using AgenStart.PackageManagement;

namespace AgenStart.Platform.Windows.WinGet;

internal sealed record WinGetPreparedPackage(
    string PreparationId,
    string RootDirectory,
    string InstallerPath,
    string InstallerType,
    IReadOnlyList<string> SilentArguments,
    IReadOnlySet<int> SuccessExitCodes)
{
    public PreparedInstallerCommand CreateInstallCommand(bool silent)
    {
        var type = InstallerType.ToLowerInvariant();
        if (type is "msi" or "wix")
        {
            var systemDirectory = Environment.SystemDirectory;
            if (string.IsNullOrWhiteSpace(systemDirectory))
            {
                throw new InvalidOperationException("Windows system directory is unavailable.");
            }

            var arguments = new List<string> { "/i", InstallerPath };
            if (silent)
            {
                if (SilentArguments.Count > 0)
                {
                    arguments.AddRange(SilentArguments);
                }
                else
                {
                    arguments.Add("/qn");
                    arguments.Add("/norestart");
                }
            }

            return new PreparedInstallerCommand(
                Path.Combine(systemDirectory, "msiexec.exe"),
                arguments);
        }

        return new PreparedInstallerCommand(
            InstallerPath,
            silent ? SilentArguments : Array.Empty<string>());
    }
}

internal sealed record PreparedInstallerCommand(
    string ExecutablePath,
    IReadOnlyList<string> Arguments);

internal sealed record WinGetPreparedPackageInspection(
    PackagePreparationStatus Status,
    WinGetPreparedPackage? Package = null,
    long? BytesDownloaded = null,
    string? DiagnosticCode = null,
    string? Message = null);

internal static class WinGetPreparedPackageInspector
{
    private static readonly HashSet<string> SupportedInstallerTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "exe",
            "inno",
            "nullsoft",
            "burn",
            "msi",
            "wix"
        };

    public static WinGetPreparedPackageInspection Inspect(
        string rootDirectory,
        string expectedPackageId,
        bool requireSilent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPackageId);

        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            return Failure(
                "winget.preparation-output-missing",
                "WinGet completed without a preparation directory.");
        }

        foreach (var manifestPath in Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
        {
            if (!IsWithinRoot(root, manifestPath))
            {
                continue;
            }

            ParsedManifest manifest;
            try
            {
                manifest = ParseManifest(File.ReadAllLines(manifestPath));
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (!string.Equals(
                    manifest.PackageIdentifier,
                    expectedPackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (manifest.HasDependencies)
            {
                return Unsupported(
                    "winget.preparation-dependencies",
                    "This package declares dependencies, so AgenStart will let WinGet install it through the normal trusted path.");
            }

            if (string.IsNullOrWhiteSpace(manifest.InstallerSha256))
            {
                return Failure(
                    "winget.preparation-hash-missing",
                    "The prepared WinGet manifest did not contain an installer hash.");
            }

            var installerPath = FindHashMatchedInstaller(root, manifest.InstallerSha256);
            if (installerPath is null)
            {
                return new WinGetPreparedPackageInspection(
                    PackagePreparationStatus.IntegrityFailure,
                    DiagnosticCode: "winget.preparation-hash-mismatch",
                    Message: "AgenStart could not match the downloaded installer to the hash verified by the WinGet manifest.");
            }

            var installerType = ResolveInstallerType(manifest.InstallerType, installerPath);
            if (!SupportedInstallerTypes.Contains(installerType))
            {
                return Unsupported(
                    "winget.preparation-installer-type-unsupported",
                    $"Prepared installation is not enabled for WinGet installer type '{installerType}'. The normal WinGet install path will be used.");
            }

            var silentArguments = ResolveSilentArguments(installerType, manifest.SilentSwitch);
            if (requireSilent && silentArguments.Count == 0 && installerType is not "msi" and not "wix")
            {
                return Unsupported(
                    "winget.preparation-silent-switch-unavailable",
                    "The trusted manifest does not expose a safe silent invocation for this installer, so the normal WinGet install path will be used.");
            }

            var preparationId = Guid.NewGuid().ToString("N");
            var successCodes = new HashSet<int>(manifest.SuccessExitCodes) { 0 };
            var bytesDownloaded = new FileInfo(installerPath).Length;

            return new WinGetPreparedPackageInspection(
                PackagePreparationStatus.Ready,
                new WinGetPreparedPackage(
                    preparationId,
                    root,
                    installerPath,
                    installerType,
                    silentArguments,
                    successCodes),
                bytesDownloaded,
                Message: "Installer hash verified; package is ready for sequential installation.");
        }

        return Failure(
            "winget.preparation-manifest-missing",
            "WinGet did not emit a merged manifest for the requested exact package.");
    }

    private static ParsedManifest ParseManifest(IReadOnlyList<string> lines)
    {
        string? packageIdentifier = null;
        string? installerType = null;
        string? installerSha256 = null;
        string? silent = null;
        var successCodes = new HashSet<int>();
        var hasDependencies = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var raw = lines[index];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (packageIdentifier is null && TryReadValue(trimmed, "PackageIdentifier", out var packageValue))
            {
                packageIdentifier = Unquote(packageValue);
                continue;
            }

            if (installerType is null && TryReadValue(trimmed, "InstallerType", out var typeValue))
            {
                installerType = Unquote(typeValue);
                continue;
            }

            if (installerSha256 is null && TryReadValue(trimmed, "InstallerSha256", out var hashValue))
            {
                installerSha256 = Unquote(hashValue);
                continue;
            }

            if (trimmed.Equals("Dependencies:", StringComparison.OrdinalIgnoreCase))
            {
                hasDependencies = true;
                continue;
            }

            if (trimmed.Equals("InstallerSwitches:", StringComparison.OrdinalIgnoreCase))
            {
                var sectionIndent = LeadingWhitespace(raw);
                for (var cursor = index + 1; cursor < lines.Count; cursor++)
                {
                    var candidateRaw = lines[cursor];
                    if (string.IsNullOrWhiteSpace(candidateRaw))
                    {
                        continue;
                    }

                    if (LeadingWhitespace(candidateRaw) <= sectionIndent)
                    {
                        break;
                    }

                    var candidate = candidateRaw.Trim();
                    if (TryReadValue(candidate, "Silent", out var silentValue))
                    {
                        silent = Unquote(silentValue);
                    }
                    else if (silent is null &&
                             TryReadValue(candidate, "SilentWithProgress", out var progressValue))
                    {
                        silent = Unquote(progressValue);
                    }
                }

                continue;
            }

            if (trimmed.Equals("InstallerSuccessCodes:", StringComparison.OrdinalIgnoreCase))
            {
                var sectionIndent = LeadingWhitespace(raw);
                for (var cursor = index + 1; cursor < lines.Count; cursor++)
                {
                    var candidateRaw = lines[cursor];
                    if (string.IsNullOrWhiteSpace(candidateRaw))
                    {
                        continue;
                    }

                    if (LeadingWhitespace(candidateRaw) <= sectionIndent)
                    {
                        break;
                    }

                    var candidate = candidateRaw.Trim();
                    if (candidate.StartsWith("-", StringComparison.Ordinal))
                    {
                        var numeric = candidate[1..].Trim();
                        if (int.TryParse(numeric, out var exitCode))
                        {
                            successCodes.Add(exitCode);
                        }
                    }
                }
            }
        }

        return new ParsedManifest(
            packageIdentifier,
            installerType,
            installerSha256,
            silent,
            successCodes,
            hasDependencies);
    }

    private static string? FindHashMatchedInstaller(string root, string expectedHash)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!IsWithinRoot(root, file) ||
                string.Equals(Path.GetExtension(file), ".yaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(file);
                var actualHash = Convert.ToHexString(SHA256.HashData(stream));
                if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(file);
                }
            }
            catch (IOException)
            {
                // Ignore files that are still transient or cannot be read safely.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore inaccessible dependency/license files.
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveSilentArguments(
        string installerType,
        string? manifestSwitch)
    {
        if (!string.IsNullOrWhiteSpace(manifestSwitch))
        {
            return SplitArguments(manifestSwitch);
        }

        return installerType.ToLowerInvariant() switch
        {
            "inno" => ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-"],
            "nullsoft" => ["/S"],
            "burn" => ["/quiet", "/norestart"],
            "msi" or "wix" => ["/qn", "/norestart"],
            _ => Array.Empty<string>()
        };
    }

    internal static IReadOnlyList<string> SplitArguments(string commandLine)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quote = '\0';
        var escaping = false;

        foreach (var character in commandLine)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaping = true;
                continue;
            }

            if (character is '"' or '\'')
            {
                if (inQuotes && character == quote)
                {
                    inQuotes = false;
                    quote = '\0';
                }
                else if (!inQuotes)
                {
                    inQuotes = true;
                    quote = character;
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (escaping)
        {
            current.Append('\\');
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string ResolveInstallerType(string? manifestType, string installerPath)
    {
        if (!string.IsNullOrWhiteSpace(manifestType))
        {
            return manifestType.Trim();
        }

        return Path.GetExtension(installerPath).ToLowerInvariant() switch
        {
            ".msi" => "msi",
            ".exe" => "exe",
            ".msix" => "msix",
            ".msixbundle" => "msixbundle",
            ".appx" => "appx",
            ".appxbundle" => "appxbundle",
            _ => "unknown"
        };
    }

    private static bool TryReadValue(string line, string key, out string value)
    {
        var prefix = key + ":";
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed
            .Replace("''", "'", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static int LeadingWhitespace(string value)
    {
        var count = 0;
        while (count < value.Length && char.IsWhiteSpace(value[count]))
        {
            count++;
        }

        return count;
    }

    private static bool IsWithinRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static WinGetPreparedPackageInspection Unsupported(string code, string message) =>
        new(PackagePreparationStatus.Unsupported, DiagnosticCode: code, Message: message);

    private static WinGetPreparedPackageInspection Failure(string code, string message) =>
        new(PackagePreparationStatus.Failed, DiagnosticCode: code, Message: message);

    private sealed record ParsedManifest(
        string? PackageIdentifier,
        string? InstallerType,
        string? InstallerSha256,
        string? SilentSwitch,
        IReadOnlySet<int> SuccessExitCodes,
        bool HasDependencies);
}
