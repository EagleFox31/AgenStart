using AgenStart.Uninstall;

namespace AgenStart.Desktop.LocalData;

/// <summary>The leftover rules shipped with the app, read like the catalogue: file next to the app, else embedded.</summary>
internal static class PackagedUninstallRules
{
    private const string ResourceName = "AgenStart.Data.uninstall-rules.json";

    public static UninstallRuleSet Load()
    {
        var externalPath = Path.Combine(AppContext.BaseDirectory, "Data", "uninstall-rules.json");
        using var stream = File.Exists(externalPath)
            ? File.OpenRead(externalPath)
            : typeof(PackagedUninstallRules).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidDataException(
                    $"The uninstall rules are unavailable. Expected '{externalPath}' or embedded resource '{ResourceName}'.");
        return UninstallRuleSetLoader.Load(stream);
    }
}
