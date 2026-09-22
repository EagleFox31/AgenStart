namespace AgenStart.Desktop.LocalData;

internal static class PackagedCatalogue
{
    private const string ResourceName = "AgenStart.Data.catalogue.json";

    public static Stream OpenRead()
    {
        var externalPath = Path.Combine(AppContext.BaseDirectory, "Data", "catalogue.json");
        if (File.Exists(externalPath))
        {
            return File.OpenRead(externalPath);
        }

        return typeof(PackagedCatalogue).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FileNotFoundException(
                $"The AgenStart software catalogue is unavailable. Expected '{externalPath}' or embedded resource '{ResourceName}'.",
                externalPath);
    }
}
