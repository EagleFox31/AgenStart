using Avalonia.Threading;

namespace AgenStart.Desktop.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static readonly AvaloniaUiDispatcher Shared = new();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
