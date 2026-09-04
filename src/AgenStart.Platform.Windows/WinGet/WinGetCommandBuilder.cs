using System.Text.RegularExpressions;
using AgenStart.PackageManagement;

namespace AgenStart.Platform.Windows.WinGet;

public sealed record WinGetCommand(IReadOnlyList<string> Arguments);

public static partial class WinGetCommandBuilder
{
    private static readonly HashSet<string> AllowedSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "winget",
        "msstore"
    };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    public static WinGetCommand BuildResolve(ProviderPackageReference package)
    {
        ValidateReference(package);

        return new WinGetCommand(
        [
            "show",
            "--id", package.PackageId,
            "--exact",
            "--source", package.Source,
            "--disable-interactivity"
        ]);
    }

    public static WinGetCommand BuildExportInstalled(string source, string outputPath)
    {
        ValidateSource(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (!Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException(
                "WinGet export output must be an AgenStart-owned absolute path.",
                nameof(outputPath));
        }

        return new WinGetCommand(
        [
            "export",
            "--output", outputPath,
            "--source", source,
            "--include-versions",
            "--disable-interactivity"
        ]);
    }

    public static WinGetCommand BuildDownload(
        PackageInstallRequest request,
        string downloadDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApplicationId(request.ApplicationId);
        ValidateReference(request.Package);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);

        if (!Path.IsPathFullyQualified(downloadDirectory))
        {
            throw new ArgumentException(
                "WinGet download output must be an AgenStart-owned absolute path.",
                nameof(downloadDirectory));
        }

        var arguments = new List<string>
        {
            "download",
            "--id", request.Package.PackageId,
            "--exact",
            "--source", request.Package.Source,
            "--download-directory", downloadDirectory,
            "--disable-interactivity"
        };

        AddScope(arguments, request.Package.ScopePreference, nameof(request));

        if (request.AcceptPackageAgreements)
        {
            arguments.Add("--accept-package-agreements");
        }

        if (request.AcceptSourceAgreements)
        {
            arguments.Add("--accept-source-agreements");
        }

        return new WinGetCommand(arguments);
    }

    public static WinGetCommand BuildInstall(PackageInstallRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateApplicationId(request.ApplicationId);
        ValidateReference(request.Package);

        var arguments = new List<string>
        {
            "install",
            "--id", request.Package.PackageId,
            "--exact",
            "--source", request.Package.Source,
            "--disable-interactivity",
            "--no-upgrade"
        };

        AddScope(arguments, request.Package.ScopePreference, nameof(request));

        if (request.Silent)
        {
            arguments.Add("--silent");
        }

        if (request.AcceptPackageAgreements)
        {
            arguments.Add("--accept-package-agreements");
        }

        if (request.AcceptSourceAgreements)
        {
            arguments.Add("--accept-source-agreements");
        }

        return new WinGetCommand(arguments);
    }

    public static void ValidateReference(ProviderPackageReference package)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (!string.Equals(package.ProviderId, PackageProviderIds.WinGet, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"WinGet provider cannot execute provider reference '{package.ProviderId}'.",
                nameof(package));
        }

        if (string.IsNullOrWhiteSpace(package.PackageId) || !PackageIdPattern().IsMatch(package.PackageId))
        {
            throw new ArgumentException(
                "WinGet package ID contains unsupported characters or shape.",
                nameof(package));
        }

        ValidateSource(package.Source);
    }

    private static void AddScope(
        List<string> arguments,
        PackageScope scope,
        string parameterName)
    {
        switch (scope)
        {
            case PackageScope.User:
                arguments.Add("--scope");
                arguments.Add("user");
                break;
            case PackageScope.Machine:
                arguments.Add("--scope");
                arguments.Add("machine");
                break;
            case PackageScope.Default:
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    scope,
                    "Unsupported package scope.");
        }
    }

    private static void ValidateSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || !AllowedSources.Contains(source))
        {
            throw new ArgumentException(
                $"WinGet source '{source}' is not trusted by the MVP provider policy.",
                nameof(source));
        }
    }

    private static void ValidateApplicationId(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("Canonical AgenStart application ID is required.", nameof(applicationId));
        }
    }
}
