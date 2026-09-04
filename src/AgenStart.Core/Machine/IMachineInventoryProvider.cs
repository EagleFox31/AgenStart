namespace AgenStart.Core.Machine;

public interface IMachineInventoryProvider
{
    Task<MachineSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
