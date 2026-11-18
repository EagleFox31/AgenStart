using System.Runtime.Versioning;
using System.Security;
using AgenStart.Uninstall;
using Microsoft.Win32;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Looks up one HKCU or HKLM key in the 64-bit view (WOW6432Node paths are explicit). Every key is
/// opened with <c>writable: false</c>; the returned path keeps the key's real casing.
/// </summary>
public sealed class WindowsRegistryProbe : IRegistryProbe
{
    public RegistryKeyEntry? GetKey(string keyPath)
    {
        if (!OperatingSystem.IsWindows() || RegistryPath.Normalize(keyPath) is not { } normalized)
        {
            return null;
        }

        try
        {
            return Read(normalized);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKeyEntry? Read(string normalized)
    {
        var hive = RegistryPath.Hive(normalized) == RegistryPath.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        var key = baseKey;
        var segments = new List<string> { RegistryPath.Hive(normalized) };
        try
        {
            foreach (var segment in RegistryPath.Segments(normalized))
            {
                var actual = key.GetSubKeyNames().FirstOrDefault(name => string.Equals(name, segment, StringComparison.OrdinalIgnoreCase));
                var child = actual is null ? null : key.OpenSubKey(actual, writable: false);
                if (!ReferenceEquals(key, baseKey))
                {
                    key.Dispose();
                }

                if (child is null)
                {
                    return null;
                }

                key = child;
                segments.Add(actual!);
            }

            return new RegistryKeyEntry(string.Join('\\', segments), key.SubKeyCount, key.ValueCount);
        }
        finally
        {
            if (!ReferenceEquals(key, baseKey))
            {
                key.Dispose();
            }
        }
    }
}
