using Avalonia.Media;
using AgenStart.Core.Catalogue;
using AgenStart.Desktop.Icons;
using AgenStart.Desktop.Services;
using AgenStart.Recommendations;

namespace AgenStart.Desktop.ViewModels;

public sealed class RecommendationRowViewModel : LocalizedRow
{
    // Keep badge hues intentionally far apart so users can scan the list without
    // having to read every label: blue = recommendation, green = installed,
    // violet = gem, red = attention.
    private static readonly IBrush RecommendedBrush = new SolidColorBrush(Color.Parse("#1D4ED8"));
    private static readonly IBrush InstalledBrush = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush GemBrush = new SolidColorBrush(Color.Parse("#6D3FB5"));
    private static readonly IBrush AttentionBrush = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush SoftRecommendedBrush = new SolidColorBrush(Color.Parse("#E8F0FE"));
    private static readonly IBrush SoftInstalledBrush = new SolidColorBrush(Color.Parse("#E6F4EA"));
    private static readonly IBrush SoftGemBrush = new SolidColorBrush(Color.Parse("#F0E8FA"));
    private static readonly IBrush SoftAttentionBrush = new SolidColorBrush(Color.Parse("#FDECEA"));
    private static readonly IBrush ReviewBrush = new SolidColorBrush(Color.Parse("#9A6700"));
    private static readonly IBrush SoftReviewBrush = new SolidColorBrush(Color.Parse("#FFF4D6"));
    private static readonly IBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);

    private readonly string? _statusKey;
    private readonly string? _statusDetailKey;
    private bool _isSelected;
    private string _orderLabel = string.Empty;

    public RecommendationRowViewModel(
        RecommendationDecision decision,
        string description)
    {
        ApplicationId = decision.ApplicationId;
        Name = decision.ApplicationName;
        Description = description;

        // The card's first explanatory line must answer “what is this for?” in plain language.
        Reason = description;
        WhyRecommended = string.Join(
            " · ",
            decision.Reasons
                .Where(reason => reason.Code.StartsWith("profile.", StringComparison.Ordinal))
                .Select(reason => reason.Message)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        Level = decision.Level;
        Disposition = decision.Disposition;
        CanSelect = decision.Disposition == RecommendationDisposition.Recommended;
        _isSelected = CanSelect && decision.SelectedByDefault;
        _statusKey = BuildStatusKey(decision);
        StatusIcon = BuildStatusIcon(decision);
        _statusDetailKey = BuildStatusDetailKey(decision);
        (StatusBrush, StatusBackgroundBrush) = BuildStatusBrushes(decision);
        Initials = BuildInitials(decision.ApplicationName);
        IconSource = AppIconService.Shared.Resolve(decision.ApplicationId);
    }

    public string ApplicationId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Reason { get; }
    public string WhyRecommended { get; }
    public string Initials { get; }
    public IImage? IconSource { get; }
    public bool HasLogo => IconSource is not null;
    public bool ShowInitials => IconSource is null;
    public RecommendationLevel Level { get; }
    public RecommendationDisposition Disposition { get; }
    public bool CanSelect { get; }
    public string Status => _statusKey is null ? string.Empty : Text[_statusKey];
    public string StatusIcon { get; }
    public string? StatusDetail => _statusDetailKey is null ? null : Text[_statusDetailKey];
    public IBrush StatusBrush { get; }
    public IBrush StatusBackgroundBrush { get; }

    public string OrderLabel
    {
        get => _orderLabel;
        set
        {
            if (_orderLabel == value)
            {
                return;
            }

            _orderLabel = value;
            OnPropertyChanged(nameof(OrderLabel));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect || _isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    private static string? BuildStatusKey(RecommendationDecision decision)
    {
        if (decision.Disposition == RecommendationDisposition.AlreadyInstalled)
        {
            return "Recommendations.Badge.Installed";
        }

        if (IsUnverifiedState(decision.Disposition))
        {
            return "Recommendations.Badge.NeedsReview";
        }

        if (IsAttentionState(decision.Disposition))
        {
            return "Recommendations.Badge.Attention";
        }

        return decision.Level switch
        {
            RecommendationLevel.Recommended => "Recommendations.Badge.Recommended",
            RecommendationLevel.Gem => "Recommendations.Badge.Gem",
            // Essential is communicated by ranking + default selection.
            // Optional is intentionally unbadged to keep the list quiet.
            _ => null
        };
    }

    private static string BuildStatusIcon(RecommendationDecision decision)
    {
        if (decision.Disposition == RecommendationDisposition.AlreadyInstalled)
        {
            return "✓";
        }

        if (IsAttentionState(decision.Disposition))
        {
            return "!";
        }

        return decision.Level switch
        {
            RecommendationLevel.Recommended => "✦",
            RecommendationLevel.Gem => "◆",
            _ => string.Empty
        };
    }

    private static string? BuildStatusDetailKey(RecommendationDecision decision) => decision.Disposition switch
    {
        RecommendationDisposition.Incompatible => "Recommendations.Detail.Incompatible",
        RecommendationDisposition.CompatibilityUnknown => "Recommendations.Detail.CompatibilityUnknown",
        RecommendationDisposition.InventoryUnknown => "Recommendations.Detail.InventoryUnknown",
        RecommendationDisposition.Conflict => "Recommendations.Detail.Conflict",
        RecommendationDisposition.Unavailable => "Recommendations.Detail.Unavailable",
        _ => null
    };

    private static (IBrush Foreground, IBrush Background) BuildStatusBrushes(RecommendationDecision decision)
    {
        if (decision.Disposition == RecommendationDisposition.AlreadyInstalled)
        {
            return (InstalledBrush, SoftInstalledBrush);
        }

        if (IsUnverifiedState(decision.Disposition))
        {
            return (ReviewBrush, SoftReviewBrush);
        }

        if (IsAttentionState(decision.Disposition))
        {
            return (AttentionBrush, SoftAttentionBrush);
        }

        return decision.Level switch
        {
            RecommendationLevel.Recommended => (RecommendedBrush, SoftRecommendedBrush),
            RecommendationLevel.Gem => (GemBrush, SoftGemBrush),
            // No visible pill for Essential or Optional. The existing border remains
            // layout-neutral because both its text and background are transparent.
            RecommendationLevel.Essential => (TransparentBrush, TransparentBrush),
            RecommendationLevel.Optional => (TransparentBrush, TransparentBrush),
            _ => (TransparentBrush, TransparentBrush)
        };
    }

    // Unknown states are not failures: AgenStart only lacks proof, so they read as a softer warning.
    private static bool IsUnverifiedState(RecommendationDisposition disposition) =>
        disposition is RecommendationDisposition.CompatibilityUnknown
            or RecommendationDisposition.InventoryUnknown;

    private static bool IsAttentionState(RecommendationDisposition disposition) =>
        disposition is RecommendationDisposition.Incompatible
            or RecommendationDisposition.CompatibilityUnknown
            or RecommendationDisposition.InventoryUnknown
            or RecommendationDisposition.Conflict
            or RecommendationDisposition.Unavailable;

    private static string BuildInitials(string name)
    {
        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        return words.Length switch
        {
            0 => "•",
            1 => words[0][..1].ToUpperInvariant(),
            _ => string.Concat(words.Select(word => char.ToUpperInvariant(word[0])))
        };
    }
}
