namespace AgenStart.SoftwareInventory;

public static class SoftwareInventorySourceIds
{
    public const string WindowsRegistry = "registry:windows";

    public static string ForPackageProvider(string providerId, string source) =>
        $"provider:{Normalize(providerId)}:{Normalize(source)}";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();
}
