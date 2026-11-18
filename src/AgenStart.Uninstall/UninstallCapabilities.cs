namespace AgenStart.Uninstall;

public static class UninstallCapabilities
{
    /// <summary>
    /// True when an unattended removal is likely: a quiet command is registered, or the installer
    /// technology has a documented silent switch (MSI with a product code, Inno Setup, NSIS).
    /// This is an indication for display only; nothing is executed.
    /// </summary>
    public static bool IsSilentUninstallLikely(InstalledProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        return program.QuietUninstallString is not null
            || program.InstallerKind switch
            {
                InstallerKind.Msi => program.MsiProductCode is not null,
                InstallerKind.Inno or InstallerKind.Nsis => true,
                _ => false
            };
    }
}
