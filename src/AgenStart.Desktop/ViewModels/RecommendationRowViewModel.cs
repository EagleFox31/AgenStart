using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Core.Catalogue;
using AgenStart.Recommendations;

namespace AgenStart.Desktop.ViewModels;

public sealed class RecommendationRowViewModel : INotifyPropertyChanged
{
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
        Initials = BuildInitials(decision.ApplicationName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApplicationId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Reason { get; }
    public string Initials { get; }
    public RecommendationLevel Level { get; }
    public RecommendationDisposition Disposition { get; }
    public bool CanSelect { get; }
    public string Status { get; }

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
