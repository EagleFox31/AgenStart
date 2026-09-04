namespace AgenStart.Core.Recommendations;

public enum RecommendationPipelineStage
{
    LoadingTrustedCatalogue,
    ReadingInstalledApplications,
    ApplyingInstalledStateRules,
    MatchingSelectedProfile,
    FinalizingRecommendations
}

public static class RecommendationPipelineDiagnostics
{
    private static readonly object Sync = new();
    private static Action<RecommendationPipelineStage>? _stageChanged;

    public static IDisposable Subscribe(Action<RecommendationPipelineStage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (Sync)
        {
            _stageChanged += handler;
        }

        return new Subscription(handler);
    }

    public static void Report(RecommendationPipelineStage stage)
    {
        Action<RecommendationPipelineStage>? handlers;
        lock (Sync)
        {
            handlers = _stageChanged;
        }

        if (handlers is null)
        {
            return;
        }

        foreach (Action<RecommendationPipelineStage> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(stage);
            }
            catch
            {
                // UI diagnostics must never be able to break recommendation generation.
            }
        }
    }

    private sealed class Subscription(Action<RecommendationPipelineStage> handler) : IDisposable
    {
        private Action<RecommendationPipelineStage>? _handler = handler;

        public void Dispose()
        {
            var handlerToRemove = Interlocked.Exchange(ref _handler, null);
            if (handlerToRemove is null)
            {
                return;
            }

            lock (Sync)
            {
                _stageChanged -= handlerToRemove;
            }
        }
    }
}
