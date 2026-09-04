using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AgenStart.Core.Machine;
using AgenStart.Platform.Windows.WinGet;
using Microsoft.Win32;

namespace AgenStart.Platform.Windows.Inventory;

public sealed class WindowsMachineInventoryProvider : IMachineInventoryProvider
{
    private static readonly TimeSpan WinGetProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IWinGetExecutableLocator _winGetLocator;
    private readonly IWinGetProcessRunner _winGetRunner;
    private readonly TimeProvider _timeProvider;

    public WindowsMachineInventoryProvider(
        IWinGetExecutableLocator? winGetLocator = null,
        IWinGetProcessRunner? winGetRunner = null,
        TimeProvider? timeProvider = null)
    {
        _winGetLocator = winGetLocator ?? new WinGetExecutableLocator();
        _winGetRunner = winGetRunner ?? new WinGetProcessRunner();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<MachineSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(CreateUnsupportedSnapshot());
        }

        return CaptureWindowsAsync(cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private async Task<MachineSnapshot> CaptureWindowsAsync(CancellationToken cancellationToken)
    {
        var diagnostics = new List<InventoryDiagnostic>();

        cancellationToken.ThrowIfCancellationRequested();
        var platform = CollectPlatform(diagnostics);

        cancellationToken.ThrowIfCancellationRequested();
        var cpu = CollectCpu(platform.Architecture, diagnostics);

        cancellationToken.ThrowIfCancellationRequested();
        var memory = CollectMemory(diagnostics);

        cancellationToken.ThrowIfCancellationRequested();
        var storage = CollectStorage(diagnostics);

        cancellationToken.ThrowIfCancellationRequested();
        var (gpus, gpuState) = CollectGpus(diagnostics);

        var packageManager = await CollectWinGetAsync(diagnostics, cancellationToken).ConfigureAwait(false);

        return new MachineSnapshot(
            platform,
            cpu,
            memory,
            gpus,
            storage,
            packageManager,
            new CapabilitySnapshot(gpuState),
            diagnostics,
            _timeProvider.GetUtcNow());
    }

    [SupportedOSPlatform("windows")]
    private static PlatformSnapshot CollectPlatform(ICollection<InventoryDiagnostic> diagnostics)
    {
        string? edition = null;
        string? displayVersion = null;
        string? build = null;
        string? revision = null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                writable: false);

            edition = ReadRegistryString(key, "ProductName") ?? ReadRegistryString(key, "EditionID");
            displayVersion = ReadRegistryString(key, "DisplayVersion") ?? ReadRegistryString(key, "ReleaseId");
            build = ReadRegistryString(key, "CurrentBuildNumber") ?? ReadRegistryString(key, "CurrentBuild");
            revision = key?.GetValue("UBR")?.ToString();
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            diagnostics.Add(new InventoryDiagnostic(
                "platform.registry-partial",
                "Some Windows edition/build metadata could not be read as the current user."));
        }

        var osVersion = Environment.OSVersion.Version;
        return new PlatformSnapshot(
            PlatformKind.Windows,
            edition,
            displayVersion,
            osVersion,
            build ?? osVersion.Build.ToString(),
            revision,
            MapArchitecture(RuntimeInformation.OSArchitecture),
            MapArchitecture(RuntimeInformation.ProcessArchitecture));
    }

    [SupportedOSPlatform("windows")]
    private static CpuSnapshot CollectCpu(
        MachineArchitecture architecture,
        ICollection<InventoryDiagnostic> diagnostics)
    {
        string? model = null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                writable: false);
            model = ReadRegistryString(key, "ProcessorNameString")?.Trim();
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            diagnostics.Add(new InventoryDiagnostic(
                "cpu.model-unavailable",
                "The CPU model name could not be read; processor count and architecture remain available."));
        }

        return new CpuSnapshot(model, architecture, Environment.ProcessorCount);
    }

    [SupportedOSPlatform("windows")]
    private static MemorySnapshot CollectMemory(ICollection<InventoryDiagnostic> diagnostics)
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            diagnostics.Add(new InventoryDiagnostic(
                "memory.query-failed",
                "Windows could not provide physical memory information."));
            return new MemorySnapshot(null, null);
        }

        return new MemorySnapshot(status.TotalPhysical, status.AvailablePhysical);
    }

    private static IReadOnlyList<StorageSnapshot> CollectStorage(ICollection<InventoryDiagnostic> diagnostics)
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        var result = new List<StorageSnapshot>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    {
                        continue;
                    }

                    var root = drive.RootDirectory.FullName;
                    result.Add(new StorageSnapshot(
                        root,
                        StorageKind.Fixed,
                        drive.TotalSize,
                        drive.AvailableFreeSpace,
                        string.Equals(root, systemRoot, StringComparison.OrdinalIgnoreCase)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new InventoryDiagnostic(
                        "storage.drive-partial",
                        "One local drive could not be queried and was skipped."));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new InventoryDiagnostic(
                "storage.enumeration-failed",
                "Local storage could not be enumerated."));
        }

        return result;
    }

    [SupportedOSPlatform("windows")]
    private static (IReadOnlyList<GpuSnapshot> Gpus, GpuCapabilityState State) CollectGpus(
        ICollection<InventoryDiagnostic> diagnostics)
    {
        var results = new Dictionary<string, GpuSnapshot>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Video",
                writable: false);

            if (videoKey is null)
            {
                return ([], GpuCapabilityState.Unknown);
            }

            foreach (var adapterKeyName in videoKey.GetSubKeyNames())
            {
                using var adapterKey = videoKey.OpenSubKey(adapterKeyName, writable: false);
                if (adapterKey is null)
                {
                    continue;
                }

                foreach (var instanceName in adapterKey.GetSubKeyNames())
                {
                    if (!int.TryParse(instanceName, out _))
                    {
                        continue;
                    }

                    using var instanceKey = adapterKey.OpenSubKey(instanceName, writable: false);
                    var name = ReadRegistryString(instanceKey, "DriverDesc")?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var vendor = ReadRegistryString(instanceKey, "ProviderName")?.Trim();
                    results.TryAdd(name, new GpuSnapshot(name, vendor));
                }
            }
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            diagnostics.Add(new InventoryDiagnostic(
                "gpu.query-failed",
                "GPU information could not be read. GPU-dependent compatibility remains unverified."));
            return ([], GpuCapabilityState.Unknown);
        }

        if (results.Count == 0)
        {
            diagnostics.Add(new InventoryDiagnostic(
                "gpu.not-determined",
                "No display adapter could be identified through the best-effort Windows inventory source."));
            return ([], GpuCapabilityState.Unknown);
        }

        return (results.Values.OrderBy(gpu => gpu.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            GpuCapabilityState.Available);
    }

    private async Task<PackageManagerSnapshot> CollectWinGetAsync(
        ICollection<InventoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var resolution = _winGetLocator.Resolve();
        if (!resolution.Found || string.IsNullOrWhiteSpace(resolution.Path))
        {
            if (!string.IsNullOrWhiteSpace(resolution.DiagnosticCode))
            {
                diagnostics.Add(new InventoryDiagnostic(
                    resolution.DiagnosticCode,
                    resolution.Message ?? "WinGet is unavailable."));
            }

            return new PackageManagerSnapshot(
                PackageManagerKind.WinGet,
                CapabilityState.Unavailable,
                null);
        }

        var result = await _winGetRunner.RunAsync(
            resolution.Path,
            ["--version"],
            WinGetProbeTimeout,
            cancellationToken).ConfigureAwait(false);

        if (result.Cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (result.TimedOut)
        {
            diagnostics.Add(new InventoryDiagnostic(
                "winget.version-timeout",
                "WinGet did not respond within the inventory timeout."));
            return new PackageManagerSnapshot(PackageManagerKind.WinGet, CapabilityState.TimedOut, null);
        }

        if (!result.Started || result.ExitCode != 0)
        {
            diagnostics.Add(new InventoryDiagnostic(
                "winget.version-failed",
                "WinGet was found but its version could not be queried successfully."));
            return new PackageManagerSnapshot(PackageManagerKind.WinGet, CapabilityState.Failed, null);
        }

        var version = ParseWinGetVersion(result.StandardOutput);
        if (version is null)
        {
            diagnostics.Add(new InventoryDiagnostic(
                "winget.version-unparsed",
                "WinGet is available but its version text could not be normalized."));
        }

        return new PackageManagerSnapshot(
            PackageManagerKind.WinGet,
            CapabilityState.Available,
            version);
    }

    public static Version? ParseWinGetVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var token = output
            .Trim()
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        token = token.TrimStart('v', 'V');
        var dash = token.IndexOf('-');
        if (dash >= 0)
        {
            token = token[..dash];
        }

        return Version.TryParse(token, out var version) ? version : null;
    }

    private MachineSnapshot CreateUnsupportedSnapshot() =>
        new(
            new PlatformSnapshot(
                PlatformKind.Unknown,
                null,
                null,
                Environment.OSVersion.Version,
                null,
                null,
                MapArchitecture(RuntimeInformation.OSArchitecture),
                MapArchitecture(RuntimeInformation.ProcessArchitecture)),
            new CpuSnapshot(null, MapArchitecture(RuntimeInformation.ProcessArchitecture), Environment.ProcessorCount),
            new MemorySnapshot(null, null),
            [],
            [],
            new PackageManagerSnapshot(PackageManagerKind.None, CapabilityState.Unavailable, null),
            new CapabilitySnapshot(GpuCapabilityState.Unknown),
            [new InventoryDiagnostic(
                "inventory.unsupported-platform",
                "The Windows machine inventory provider can only collect a full snapshot on Windows.")],
            _timeProvider.GetUtcNow());

    private static MachineArchitecture MapArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X86 => MachineArchitecture.X86,
        Architecture.X64 => MachineArchitecture.X64,
        Architecture.Arm64 => MachineArchitecture.Arm64,
        _ => MachineArchitecture.Unknown
    };

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryString(RegistryKey? key, string name) =>
        key?.GetValue(name) as string;

    private static bool IsExpectedReadFailure(Exception exception) =>
        exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
