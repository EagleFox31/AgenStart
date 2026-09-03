namespace AgenStart.Core.Machine;

public enum PlatformKind
{
    Unknown,
    Windows,
    MacOS
}

public enum MachineArchitecture
{
    Unknown,
    X86,
    X64,
    Arm64
}

public enum StorageKind
{
    Unknown,
    Fixed,
    Removable,
    Network
}

public enum PackageManagerKind
{
    None,
    WinGet,
    Homebrew
}

public enum CapabilityState
{
    Available,
    Unavailable,
    Unknown,
    Failed,
    TimedOut
}

public enum GpuCapabilityState
{
    Available,
    Unavailable,
    Unknown
}

public sealed record MachineSnapshot(
    PlatformSnapshot Platform,
    CpuSnapshot Cpu,
    MemorySnapshot Memory,
    IReadOnlyList<GpuSnapshot> Gpus,
    IReadOnlyList<StorageSnapshot> Storage,
    PackageManagerSnapshot PackageManager,
    CapabilitySnapshot Capabilities,
    IReadOnlyList<InventoryDiagnostic> Diagnostics,
    DateTimeOffset CapturedAtUtc)
{
    public StorageSnapshot? SystemDrive =>
        Storage.FirstOrDefault(static storage => storage.IsSystemDrive);
}

public sealed record PlatformSnapshot(
    PlatformKind Kind,
    string? Edition,
    string? DisplayVersion,
    Version? Version,
    string? Build,
    string? Revision,
    MachineArchitecture Architecture,
    MachineArchitecture ProcessArchitecture);

public sealed record CpuSnapshot(
    string? Model,
    MachineArchitecture Architecture,
    int LogicalProcessorCount);

public sealed record MemorySnapshot(
    ulong? TotalPhysicalBytes,
    ulong? AvailablePhysicalBytes);

public sealed record GpuSnapshot(
    string? Name,
    string? Vendor);

public sealed record StorageSnapshot(
    string Root,
    StorageKind Kind,
    long? TotalBytes,
    long? AvailableBytes,
    bool IsSystemDrive);

public sealed record PackageManagerSnapshot(
    PackageManagerKind Kind,
    CapabilityState State,
    Version? Version);

public sealed record CapabilitySnapshot(
    GpuCapabilityState Gpu);

public sealed record InventoryDiagnostic(
    string Code,
    string Message);
