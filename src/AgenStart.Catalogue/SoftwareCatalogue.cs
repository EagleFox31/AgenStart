using AgenStart.Core.Catalogue;
using AgenStart.PackageManagement;
using AgenStart.SoftwareInventory;

namespace AgenStart.Catalogue;

public sealed record SoftwareCatalogue(
    string SchemaVersion,
    string CatalogueVersion,
    IReadOnlyList<CatalogueApplication> Applications)
{
    public IReadOnlyList<ApplicationDefinition> Definitions =>
        Applications.Select(static application => application.Definition).ToArray();

    public IReadOnlyList<SoftwareDetectionTarget> DetectionTargets =>
        Applications.Select(static application => application.DetectionTarget).ToArray();
}

public sealed record CatalogueApplication(
    ApplicationDefinition Definition,
    string Publisher,
    string Description,
    IReadOnlyList<ProviderPackageReference> ProviderPackages,
    IReadOnlyList<string> RegistryDisplayNames)
{
    public string Id => Definition.Id;
    public string Name => Definition.Name;

    public SoftwareDetectionTarget DetectionTarget => new(
        Definition.Id,
        Definition.Name,
        Publisher,
        ProviderPackages,
        RegistryDisplayNames);

    public ProviderPackageReference? WindowsPackage =>
        ProviderPackages.FirstOrDefault(static package =>
            string.Equals(package.ProviderId, PackageProviderIds.WinGet, StringComparison.OrdinalIgnoreCase));
}
