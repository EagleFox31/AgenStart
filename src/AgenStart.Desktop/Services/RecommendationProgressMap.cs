using AgenStart.Core.Recommendations;

namespace AgenStart.Desktop.Services;

/// <summary>One visible step of the recommendation build: bar value, label key, and whether progress is measurable.</summary>
public sealed record RecommendationProgressStep(double Percent, string LabelKey, bool Indeterminate = false);

/// <summary>
/// Maps pipeline stages to what the progress bar shows. Kept out of the view so the
/// mapping and its string keys are covered by tests.
/// </summary>
public static class RecommendationProgressMap
{
    public static RecommendationProgressStep Starting { get; } = new(10, "Recommendations.Progress.Starting");

    public static RecommendationProgressStep Ready { get; } = new(100, "Recommendations.Progress.Ready");

    public static RecommendationProgressStep? For(RecommendationPipelineStage stage) => stage switch
    {
        RecommendationPipelineStage.LoadingTrustedCatalogue => new(25, "Recommendations.Progress.LoadingCatalogue"),
        // The inventory scan has no measurable progress: show activity, not a fake percentage.
        RecommendationPipelineStage.ReadingInstalledApplications => new(50, "Recommendations.Progress.ReadingApps", Indeterminate: true),
        RecommendationPipelineStage.EvaluatingRecommendationRules => new(75, "Recommendations.Progress.ApplyingRules"),
        RecommendationPipelineStage.FinalizingRecommendations => new(90, "Recommendations.Progress.Finalizing"),
        _ => null
    };
}
