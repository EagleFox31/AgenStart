using System.ComponentModel;
using Avalonia.Media;
using AgenStart.Core.Catalogue;
using AgenStart.Recommendations;

namespace AgenStart.Desktop.ViewModels;

public sealed class RecommendationRowViewModel : INotifyPropertyChanged
{
    private static readonly IBrush TealBrush = new SolidColorBrush(Color.Parse("#176D64"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#2F7D5B"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#9A6700"));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#A84E3E"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#69747A"));
    private static readonly IBrush SoftTealBrush = new SolidColorBrush(Color.Parse("#E7F1EE"));
    private static readonly IBrush SoftSuccessBrush = new SolidColorBrush(Color.Parse("#E7F3EC"));
    private static readonly IBrush SoftWarningBrush = new SolidColorBrush(Color.Parse("#F7EEDB"));
    private static readonly IBrush SoftDangerBrush = new SolidColorBrush(Color.Parse("#F8E8E4"));
    private static readonly IBrush SoftNeutralBrush = new SolidColorBrush(Color.Parse("#ECEFEE"));

    private bool _isSelected;

    public RecommendationRowViewModel(
        RecommendationDecision decision,
        string description)
    {
        ApplicationId = decision.ApplicationId;
        Name = decision.ApplicationName;
        Description = description;
        Reason = decision.Reasons.FirstOrDefault()?.Message ?? "Recommended for this setup.";
        Level = decision.Level;
        Disposition = decision.Disposition;
        CanSelect = decision.Disposition == RecommendationDisposition.Recommended;
        _isSelected = CanSelect && decision.SelectedByDefault;
        Status = BuildStatus(decision);
        StatusIcon = BuildStatusIcon(decision);
        (StatusBrush, StatusBackgroundBrush) = BuildStatusBrushes(decision);
        Initials = BuildInitials(decision.ApplicationName);
        LogoAssetPath = BuildLogoAssetPath(decision.ApplicationId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApplicationId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Reason { get; }
    public string Initials { get; }
    public string? LogoAssetPath { get; }
    public bool HasLogo => LogoAssetPath is not null;
    public bool ShowInitials => !HasLogo;
    public RecommendationLevel Level { get; }
    public RecommendationDisposition Disposition { get; }
    public bool CanSelect { get; }
    public string Status { get; }
    public string StatusIcon { get; }
    public IBrush StatusBrush { get; }
    public IBrush StatusBackgroundBrush { get; }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    private static string BuildStatus(RecommendationDecision decision) => decision.Disposition switch
    {
        RecommendationDisposition.AlreadyInstalled => "Already installed",
        RecommendationDisposition.Incompatible => "Not compatible",
        RecommendationDisposition.CompatibilityUnknown => "Compatibility unknown",
        RecommendationDisposition.InventoryUnknown => "Inventory incomplete",
        RecommendationDisposition.Conflict => "Conflict",
        RecommendationDisposition.Unavailable => "Unavailable",
        _ => decision.Level switch
        {
            RecommendationLevel.Essential => "Essential",
            RecommendationLevel.Recommended => "Recommended",
            RecommendationLevel.Optional => "Optional",
            _ => "Recommended"
        }
    };

    private static string BuildStatusIcon(RecommendationDecision decision) => decision.Disposition switch
    {
        RecommendationDisposition.AlreadyInstalled => "✓",
        RecommendationDisposition.Incompatible => "!",
        RecommendationDisposition.CompatibilityUnknown => "?",
        RecommendationDisposition.InventoryUnknown => "?",
        RecommendationDisposition.Conflict => "!",
        RecommendationDisposition.Unavailable => "!",
        _ => decision.Level switch
        {
            RecommendationLevel.Essential => "◆",
            RecommendationLevel.Recommended => "✦",
            RecommendationLevel.Optional => "○",
            _ => "✦"
        }
    };

    private static (IBrush Foreground, IBrush Background) BuildStatusBrushes(RecommendationDecision decision)
    {
        if (decision.Disposition == RecommendationDisposition.AlreadyInstalled)
        {
            return (SuccessBrush, SoftSuccessBrush);
        }

        if (decision.Disposition == RecommendationDisposition.Incompatible)
        {
            return (DangerBrush, SoftDangerBrush);
        }

        if (decision.Disposition is RecommendationDisposition.CompatibilityUnknown
            or RecommendationDisposition.InventoryUnknown
            or RecommendationDisposition.Conflict
            or RecommendationDisposition.Unavailable)
        {
            return (WarningBrush, SoftWarningBrush);
        }

        return decision.Level switch
        {
            RecommendationLevel.Essential => (TealBrush, SoftTealBrush),
            RecommendationLevel.Recommended => (TealBrush, SoftTealBrush),
            RecommendationLevel.Optional => (MutedBrush, SoftNeutralBrush),
            _ => (TealBrush, SoftTealBrush)
        };
    }

    private static string? BuildLogoAssetPath(string applicationId) => applicationId switch
    {
        "obs-studio" => "/Assets/AppLogos/obs-studio.svg",
        "visual-studio-code" => "/Assets/AppLogos/visual-studio-code.svg",
        "vlc" => "/Assets/AppLogos/vlc-media-player.svg",
        _ => null
    };

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
