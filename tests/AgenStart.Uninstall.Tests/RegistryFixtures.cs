using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

internal static class RegistryFixtures
{
    public static readonly UninstallRegistryLocation Machine64 = new(ProgramScope.Machine, UninstallRegistryView.Registry64);
    public static readonly UninstallRegistryLocation Machine32 = new(ProgramScope.Machine, UninstallRegistryView.Registry32);
    public static readonly UninstallRegistryLocation User64 = new(ProgramScope.User, UninstallRegistryView.Registry64);
    public static readonly UninstallRegistryLocation User32 = new(ProgramScope.User, UninstallRegistryView.Registry32);

    public static UninstallRegistryEntry Entry(
        UninstallRegistryLocation location,
        string keyName,
        string? displayName,
        string? uninstallString = @"""C:\Program Files\App\uninstall.exe""")
    {
        var root = location switch
        {
            { Scope: ProgramScope.User } => @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            { View: UninstallRegistryView.Registry32 } => @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            _ => @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        return new UninstallRegistryEntry
        {
            Location = location,
            KeyName = keyName,
            KeyPath = $@"{root}\{keyName}",
            DisplayName = displayName,
            UninstallString = uninstallString
        };
    }

    public static UninstallRegistryProbeResult Probe(
        UninstallRegistryLocation location,
        params UninstallRegistryEntry[] entries) => new(location, entries);
}

internal sealed class FakeUninstallRegistrySource(params UninstallRegistryProbeResult[] probes) : IUninstallRegistrySource
{
    public int ReadCount { get; private set; }

    public IReadOnlyList<UninstallRegistryProbeResult> ReadAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return probes;
    }
}
