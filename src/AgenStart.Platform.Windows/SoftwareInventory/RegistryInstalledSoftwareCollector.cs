using System.Runtime.Versioning;
using System.Security;
using AgenStart.SoftwareInventory;
using Microsoft.Win32;

namespace AgenStart.Platform.Windows.SoftwareInventory;

public sealed class RegistryInstalledSoftwareCollector : IInstalledSoftwareCollector
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<InstalledSoftwareCollectionResult> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new InstalledSoftwareCollectionResult(
                [],
                [new InventorySourceStatus(
                    SoftwareInventorySourceIds.WindowsRegistry,
                    InventorySourceState.Unavailable,
                    "registry.unsupported-platform",
                    "Windows uninstall registry inventory is unavailable on this platform.")]));
        }

        return CollectWindowsAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static Task<InstalledSoftwareCollectionResult> CollectWindowsAsync(
        CancellationToken cancellationToken)
    {
        var records = new HashSet<InstalledSoftwareRecord>();
        var hadErrors = false;

        foreach (var probe in RegistryProbes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ReadProbe(probe, records, cancellationToken, ref hadErrors);
            }
            catch (Exception exception) when (IsExpectedRegistryFailure(exception))
            {
                hadErrors = true;
            }
        }

        var status = hadErrors
            ? new InventorySourceStatus(
                SoftwareInventorySourceIds.WindowsRegistry,
                InventorySourceState.Partial,
                "registry.partial-read",
                "Some installed-software registry entries could not be read as the current user.")
            : new InventorySourceStatus(
                SoftwareInventorySourceIds.WindowsRegistry,
                InventorySourceState.Complete);

        return Task.FromResult(new InstalledSoftwareCollectionResult(
            records
                .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            [status]));
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<RegistryProbe> RegistryProbes()
    {
        yield return new RegistryProbe(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            InstalledSoftwareScope.Machine);

        yield return new RegistryProbe(
            RegistryHive.LocalMachine,
            RegistryView.Registry32,
            InstalledSoftwareScope.Machine);

        yield return new RegistryProbe(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            InstalledSoftwareScope.User);

        yield return new RegistryProbe(
            RegistryHive.CurrentUser,
            RegistryView.Registry32,
            InstalledSoftwareScope.User);
    }

    [SupportedOSPlatform("windows")]
    private static void ReadProbe(
        RegistryProbe probe,
        ISet<InstalledSoftwareRecord> records,
        CancellationToken cancellationToken,
        ref bool hadErrors)
    {
        using var baseKey = RegistryKey.OpenBaseKey(probe.Hive, probe.View);
        using var uninstallKey = baseKey.OpenSubKey(UninstallPath, writable: false);
        if (uninstallKey is null)
        {
            return;
        }

        string[] subKeyNames;
        try
        {
            subKeyNames = uninstallKey.GetSubKeyNames();
        }
        catch (Exception exception) when (IsExpectedRegistryFailure(exception))
        {
            hadErrors = true;
            return;
        }

        foreach (var subKeyName in subKeyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var applicationKey = uninstallKey.OpenSubKey(subKeyName, writable: false);
                if (applicationKey is null || IsHiddenSystemComponent(applicationKey))
                {
                    continue;
                }

                var displayName = ReadString(applicationKey, "DisplayName");
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                records.Add(new InstalledSoftwareRecord(
                    InstalledSoftwareSourceKind.Registry,
                    SoftwareInventorySourceIds.WindowsRegistry,
                    displayName.Trim(),
                    ReadString(applicationKey, "Publisher"),
                    ReadString(applicationKey, "DisplayVersion"),
                    probe.Scope));
            }
            catch (Exception exception) when (IsExpectedRegistryFailure(exception))
            {
                hadErrors = true;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsHiddenSystemComponent(RegistryKey key)
    {
        var value = key.GetValue("SystemComponent", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value switch
        {
            int integer => integer == 1,
            long longInteger => longInteger == 1,
            string text => string.Equals(text.Trim(), "1", StringComparison.Ordinal),
            _ => false
        };
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadString(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static bool IsExpectedRegistryFailure(Exception exception) =>
        exception is SecurityException or UnauthorizedAccessException or IOException;

    private sealed record RegistryProbe(
        RegistryHive Hive,
        RegistryView View,
        InstalledSoftwareScope Scope);
}
