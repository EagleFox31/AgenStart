using System.Text.Json;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Machine;
using AgenStart.PackageManagement;

namespace AgenStart.Catalogue;

public sealed class SoftwareCatalogueLoader
{
    private const string SupportedSchemaVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SoftwareCatalogue Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var document = JsonSerializer.Deserialize<CatalogueDocumentDto>(stream, JsonOptions)
            ?? throw new InvalidDataException("The software catalogue is empty or invalid JSON.");

        return Map(document);
    }

    public SoftwareCatalogue Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return Load(stream);
    }

    private static SoftwareCatalogue Map(CatalogueDocumentDto document)
    {
        var schemaVersion = RequireText(document.SchemaVersion, "schemaVersion");
        if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported catalogue schema version '{schemaVersion}'. Expected {SupportedSchemaVersion}.");
        }

        var catalogueVersion = RequireText(document.CatalogueVersion, "catalogueVersion");
        if (document.Applications is null || document.Applications.Count == 0)
        {
            throw new InvalidDataException("The catalogue must contain at least one application.");
        }

        var applications = new List<CatalogueApplication>(document.Applications.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in document.Applications)
        {
            var application = MapApplication(dto);
            if (!ids.Add(application.Id))
            {
                throw new InvalidDataException($"Duplicate catalogue application id '{application.Id}'.");
            }

            applications.Add(application);
        }

        return new SoftwareCatalogue(schemaVersion, catalogueVersion, applications);
    }

    private static CatalogueApplication MapApplication(ApplicationDto dto)
    {
        var id = RequireText(dto.Id, "application.id");
        var name = RequireText(dto.Name, $"application[{id}].name");
        var publisher = RequireText(dto.Publisher, $"application[{id}].publisher");
        var description = RequireText(dto.Description, $"application[{id}].description");
        if (description.Length > 180)
        {
            throw new InvalidDataException(
                $"Application '{id}' description must stay under 180 characters and explain the app in plain language.");
        }

        var lifecycle = ParseLifecycle(dto.Lifecycle?.Status, id);

        if (dto.Recommendations is null)
        {
            throw new InvalidDataException($"Application '{id}' is missing recommendations.");
        }

        var recommendations = dto.Recommendations
            .Select(rule => new ProfileRecommendation(
                ParseProfile(rule.Profile, id),
                ParseRecommendationLevel(rule.Level, id),
                RequireText(rule.ReasonKey, $"application[{id}].recommendations.reasonKey")))
            .ToArray();

        var requirements = new ApplicationRequirements(
            MapCapabilities(dto.Requirements?.Minimum, id, "minimum"),
            MapCapabilities(dto.Requirements?.Recommended, id, "recommended"));

        if (dto.PlatformSupport is null || dto.PlatformSupport.Count == 0)
        {
            throw new InvalidDataException($"Application '{id}' is missing platform support rules.");
        }

        var platformSupport = dto.PlatformSupport
            .Select(rule => new PlatformSupportRule(
                ParsePlatform(rule.Platform, id),
                ParsePlatformSupport(rule.Status, id),
                ParseArchitectures(rule.Architectures, id, "platformSupport")))
            .ToArray();

        var providers = MapWindowsProviders(dto.Providers, id);
        var supportsWindows = platformSupport.Any(rule =>
            rule.Platform == PlatformKind.Windows && rule.Status == PlatformSupportStatus.Supported);

        if (lifecycle == ApplicationLifecycleStatus.Active && supportsWindows && providers.Count == 0)
        {
            throw new InvalidDataException(
                $"Active Windows application '{id}' has no trusted WinGet provider mapping.");
        }

        var aliases = dto.RegistryDisplayNames is { Count: > 0 }
            ? dto.RegistryDisplayNames.Select(alias => RequireText(alias, $"application[{id}].registryDisplayNames")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : [name];

        var definition = new ApplicationDefinition(
            id,
            name,
            lifecycle,
            recommendations,
            requirements,
            platformSupport,
            NormalizeIds(dto.Dependencies, id, "dependencies"),
            NormalizeIds(dto.Conflicts, id, "conflicts"));

        return new CatalogueApplication(definition, publisher, description, providers, aliases);
    }

    private static CapabilityRequirements MapCapabilities(
        CapabilityRequirementsDto? dto,
        string applicationId,
        string label)
    {
        if (dto is null)
        {
            throw new InvalidDataException($"Application '{applicationId}' is missing {label} requirements.");
        }

        if (dto.MinRamMiB is < 0 || dto.MinFreeStorageMiB is < 0)
        {
            throw new InvalidDataException($"Application '{applicationId}' contains negative {label} requirements.");
        }

        return new CapabilityRequirements(
            dto.MinRamMiB,
            dto.MinFreeStorageMiB,
            dto.GpuRequired,
            ParseArchitectures(dto.Architectures, applicationId, label));
    }

    private static IReadOnlyList<ProviderPackageReference> MapWindowsProviders(
        IReadOnlyList<ProviderDto>? providers,
        string applicationId)
    {
        if (providers is null)
        {
            return [];
        }

        var result = new List<ProviderPackageReference>();
        foreach (var provider in providers)
        {
            var platform = ParsePlatform(provider.Platform, applicationId);
            if (platform != PlatformKind.Windows)
            {
                continue;
            }

            var type = RequireText(provider.Type, $"application[{applicationId}].providers.type");
            if (!string.Equals(type, "winget", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsupported Windows package provider type '{type}' for '{applicationId}'.");
            }

            var packageId = RequireText(provider.PackageId, $"application[{applicationId}].providers.packageId");
            ValidatePackageId(packageId, applicationId);

            var source = RequireText(provider.Source, $"application[{applicationId}].providers.source").ToLowerInvariant();
            if (source is not ("winget" or "msstore"))
            {
                throw new InvalidDataException(
                    $"Untrusted WinGet source '{source}' for '{applicationId}'.");
            }

            result.Add(new ProviderPackageReference(
                PackageProviderIds.WinGet,
                packageId,
                source,
                ParseScope(provider.ScopePreference, applicationId)));
        }

        return result;
    }

    private static IReadOnlyList<string> NormalizeIds(
        IReadOnlyList<string>? values,
        string applicationId,
        string field)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(value => RequireText(value, $"application[{applicationId}].{field}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MachineArchitecture> ParseArchitectures(
        IReadOnlyList<string>? values,
        string applicationId,
        string field)
    {
        if (values is null || values.Count == 0)
        {
            throw new InvalidDataException(
                $"Application '{applicationId}' must declare at least one architecture for {field}.");
        }

        return values.Select(value => value?.Trim().ToLowerInvariant() switch
        {
            "x86" => MachineArchitecture.X86,
            "x64" => MachineArchitecture.X64,
            "arm64" => MachineArchitecture.Arm64,
            _ => throw new InvalidDataException(
                $"Unsupported architecture '{value}' for application '{applicationId}'.")
        }).Distinct().ToArray();
    }

    private static UserProfile ParseProfile(string? value, string applicationId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "personal" => UserProfile.Personal,
            "development" => UserProfile.Development,
            "business" => UserProfile.Business,
            "creative" or "creation" => UserProfile.Creative,
            "learning" or "training" or "study" => UserProfile.Learning,
            "gaming" => UserProfile.Gaming,
            _ => throw new InvalidDataException(
                $"Unsupported profile '{value}' for application '{applicationId}'.")
        };

    private static RecommendationLevel ParseRecommendationLevel(string? value, string applicationId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "essential" => RecommendationLevel.Essential,
            "recommended" => RecommendationLevel.Recommended,
            "gem" => RecommendationLevel.Gem,
            "optional" => RecommendationLevel.Optional,
            _ => throw new InvalidDataException(
                $"Unsupported recommendation level '{value}' for application '{applicationId}'.")
        };

    private static ApplicationLifecycleStatus ParseLifecycle(string? value, string applicationId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "active" => ApplicationLifecycleStatus.Active,
            "deprecated" => ApplicationLifecycleStatus.Deprecated,
            "blocked" => ApplicationLifecycleStatus.Blocked,
            _ => throw new InvalidDataException(
                $"Unsupported lifecycle status '{value}' for application '{applicationId}'.")
        };

    private static PlatformKind ParsePlatform(string? value, string applicationId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "windows" => PlatformKind.Windows,
            "macos" => PlatformKind.MacOS,
            _ => throw new InvalidDataException(
                $"Unsupported platform '{value}' for application '{applicationId}'.")
        };

    private static PlatformSupportStatus ParsePlatformSupport(string? value, string applicationId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "supported" => PlatformSupportStatus.Supported,
            "planned" => PlatformSupportStatus.Planned,
            "unsupported" => PlatformSupportStatus.Unsupported,
            _ => throw new InvalidDataException(
                $"Unsupported platform support status '{value}' for application '{applicationId}'.")
        };

    private static PackageScope ParseScope(string? value, string applicationId) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "any" or "default" => PackageScope.Default,
            "user" => PackageScope.User,
            "machine" => PackageScope.Machine,
            _ => throw new InvalidDataException(
                $"Unsupported package scope '{value}' for application '{applicationId}'.")
        };

    private static void ValidatePackageId(string packageId, string applicationId)
    {
        if (packageId[0] is '-' or '/' || packageId.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new InvalidDataException(
                $"Unsafe package id '{packageId}' for application '{applicationId}'.");
        }
    }

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Required catalogue field '{field}' is missing.");
        }

        return value.Trim();
    }

    private sealed class CatalogueDocumentDto
    {
        public string? SchemaVersion { get; init; }
        public string? CatalogueVersion { get; init; }
        public List<ApplicationDto>? Applications { get; init; }
    }

    private sealed class ApplicationDto
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Publisher { get; init; }
        public string? Description { get; init; }
        public LifecycleDto? Lifecycle { get; init; }
        public List<RecommendationDto>? Recommendations { get; init; }
        public RequirementsDto? Requirements { get; init; }
        public List<PlatformSupportDto>? PlatformSupport { get; init; }
        public List<ProviderDto>? Providers { get; init; }
        public List<string>? RegistryDisplayNames { get; init; }
        public List<string>? Dependencies { get; init; }
        public List<string>? Conflicts { get; init; }
    }

    private sealed class LifecycleDto
    {
        public string? Status { get; init; }
    }

    private sealed class RecommendationDto
    {
        public string? Profile { get; init; }
        public string? Level { get; init; }
        public string? ReasonKey { get; init; }
    }

    private sealed class RequirementsDto
    {
        public CapabilityRequirementsDto? Minimum { get; init; }
        public CapabilityRequirementsDto? Recommended { get; init; }
    }

    private sealed class CapabilityRequirementsDto
    {
        public long? MinRamMiB { get; init; }
        public long? MinFreeStorageMiB { get; init; }
        public bool GpuRequired { get; init; }
        public List<string>? Architectures { get; init; }
    }

    private sealed class PlatformSupportDto
    {
        public string? Platform { get; init; }
        public string? Status { get; init; }
        public List<string>? Architectures { get; init; }
    }

    private sealed class ProviderDto
    {
        public string? Platform { get; init; }
        public string? Type { get; init; }
        public string? PackageId { get; init; }
        public string? Source { get; init; }
        public string? ScopePreference { get; init; }
    }
}
