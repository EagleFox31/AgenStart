using System.ComponentModel;
using AgenStart.Core.Catalogue;
using AgenStart.Core.Profiles;

namespace AgenStart.Desktop.ViewModels;

public sealed class ProfileOptionViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public ProfileOptionViewModel(ProfileDefinition profile, UserProfile flag)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        Flag = flag;
        Id = profile.Id.Value;
        Label = profile.Label.En;
        Description = profile.Description?.En ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProfileDefinition Profile { get; }

    public UserProfile Flag { get; }

    public string Id { get; }

    public string Label { get; }

    public string Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
