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

        switch (request.Package.ScopePreference)
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
                    nameof(request),
                    request.Package.ScopePreference,
                    "Unsupported package scope.");
        }

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

        if (!AllowedSources.Contains(package.Source))
        {
            throw new ArgumentException(
                $"WinGet source '{package.Source}' is not trusted by the MVP provider policy.",
                nameof(package));
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
