using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using AgenStart.Uninstall;
using Microsoft.Win32;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Reads the Windows uninstall registry (HKLM and HKCU, 64-bit then 32-bit view) without ever
/// opening a key for writing. Shared by the installed-software collector and the uninstall inventory.
/// </summary>
public sealed class UninstallRegistryReader : IUninstallRegistrySource
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private const string Wow6432UninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly UninstallRegistryLocation[] Locations =
    [
        new(ProgramScope.Machine, UninstallRegistryView.Registry64),
        new(ProgramScope.Machine, UninstallRegistryView.Registry32),
        new(ProgramScope.User, UninstallRegistryView.Registry64),
        new(ProgramScope.User, UninstallRegistryView.Registry32)
    ];

    public IReadOnlyList<UninstallRegistryProbeResult> ReadAll(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows uninstall registry is only available on Windows.");
        }

        var results = new List<UninstallRegistryProbeResult>(Locations.Length);
        foreach (var location in Locations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                results.Add(ReadLocation(location, cancellationToken));
            }
            catch (Exception exception) when (IsExpectedRegistryFailure(exception))
            {
                results.Add(new UninstallRegistryProbeResult(location, [], RootUnreadable: true));
            }
        }

        return results;
    }

    /// <summary>
    /// Where the subkeys of a location are physically stored. The 32-bit view of HKLM is redirected to
    /// WOW6432Node on 64-bit Windows; the HKCU uninstall key is shared by both views.
    /// </summary>
    public static string RootKeyPath(UninstallRegistryLocation location, bool is64BitOperatingSystem)
    {
        ArgumentNullException.ThrowIfNull(location);

        return location.Scope switch
        {
            ProgramScope.Machine when location.View == UninstallRegistryView.Registry32 && is64BitOperatingSystem
                => $@"HKEY_LOCAL_MACHINE\{Wow6432UninstallPath}",
            ProgramScope.Machine => $@"HKEY_LOCAL_MACHINE\{UninstallPath}",
            ProgramScope.User => $@"HKEY_CURRENT_USER\{UninstallPath}",
            _ => throw new ArgumentOutOfRangeException(nameof(location), location.Scope, "Unknown uninstall scope.")
        };
    }

    [SupportedOSPlatform("windows")]
    private static UninstallRegistryProbeResult ReadLocation(
        UninstallRegistryLocation location,
        CancellationToken cancellationToken)
    {
        var hive = location.Scope switch
        {
            ProgramScope.Machine => RegistryHive.LocalMachine,
            ProgramScope.User => RegistryHive.CurrentUser,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location.Scope, "Unknown uninstall scope.")
        };
        var view = location.View switch
        {
            UninstallRegistryView.Registry64 => RegistryView.Registry64,
            UninstallRegistryView.Registry32 => RegistryView.Registry32,
            _ => throw new ArgumentOutOfRangeException(nameof(location), location.View, "Unknown registry view.")
        };

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var uninstallKey = baseKey.OpenSubKey(UninstallPath, writable: false);
        if (uninstallKey is null)
        {
            return new UninstallRegistryProbeResult(location, []);
        }

        string[] subKeyNames;
        try
        {
            subKeyNames = uninstallKey.GetSubKeyNames();
        }
        catch (Exception exception) when (IsExpectedRegistryFailure(exception))
        {
            return new UninstallRegistryProbeResult(location, [], RootUnreadable: true);
        }

        var rootPath = RootKeyPath(location, Environment.Is64BitOperatingSystem);
        var entries = new List<UninstallRegistryEntry>(subKeyNames.Length);
        var unreadableKeys = 0;

        foreach (var subKeyName in subKeyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var applicationKey = uninstallKey.OpenSubKey(subKeyName, writable: false);
                if (applicationKey is not null)
                {
                    entries.Add(ReadEntry(applicationKey, location, subKeyName, $@"{rootPath}\{subKeyName}"));
                }
            }
            catch (Exception exception) when (IsExpectedRegistryFailure(exception))
            {
                unreadableKeys++;
            }
        }

        return new UninstallRegistryProbeResult(location, entries, UnreadableKeyCount: unreadableKeys);
    }

    /// <summary>
    /// Reads in the order the installed-software collector always has: hidden flag, then name,
    /// publisher and version. A failure there drops the key. Secondary values are read afterwards
    /// and a failure on them only marks the entry, so it never changes what the collector reports.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static UninstallRegistryEntry ReadEntry(
        RegistryKey key,
        UninstallRegistryLocation location,
        string keyName,
        string keyPath)
    {
        var entry = new UninstallRegistryEntry { Location = location, KeyName = keyName, KeyPath = keyPath };

        if (IsOne(Read(key, "SystemComponent")))
        {
            return entry with { SystemComponent = true };
        }

        var displayName = ReadString(key, "DisplayName");
        if (displayName is null)
        {
            return entry;
        }

        entry = entry with
        {
            DisplayName = displayName,
            Publisher = ReadString(key, "Publisher"),
            DisplayVersion = ReadString(key, "DisplayVersion")
        };

        try
        {
            return entry with
            {
                InstallLocation = ReadString(key, "InstallLocation"),
                UninstallString = ReadString(key, "UninstallString"),
                QuietUninstallString = ReadString(key, "QuietUninstallString"),
                WindowsInstaller = IsOne(Read(key, "WindowsInstaller")),
                ParentKeyName = ReadString(key, "ParentKeyName"),
                ReleaseType = ReadString(key, "ReleaseType"),
                InnoSetupAppPath = ReadString(key, "Inno Setup: App Path"),
                EstimatedSizeKilobytes = ReadNumber(Read(key, "EstimatedSize")),
                InstallDate = ReadString(key, "InstallDate"),
                ValueNames = key.GetValueNames()
            };
        }
        catch (Exception exception) when (IsExpectedRegistryFailure(exception))
        {
            return entry with { HasUnreadableValues = true };
        }
    }

    [SupportedOSPlatform("windows")]
    private static object? Read(RegistryKey key, string valueName) =>
        key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

    [SupportedOSPlatform("windows")]
    private static string? ReadString(RegistryKey key, string valueName) =>
        Read(key, valueName) is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static bool IsOne(object? value) => value switch
    {
        int integer => integer == 1,
        long longInteger => longInteger == 1,
        string text => string.Equals(text.Trim(), "1", StringComparison.Ordinal),
        _ => false
    };

    private static long? ReadNumber(object? value) => value switch
    {
        // REG_DWORD comes back as a signed int: the stored value is unsigned.
        int integer => unchecked((uint)integer),
        long longInteger => longInteger,
        string text when long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null
    };

    private static bool IsExpectedRegistryFailure(Exception exception) =>
        exception is SecurityException or UnauthorizedAccessException or IOException;
}
