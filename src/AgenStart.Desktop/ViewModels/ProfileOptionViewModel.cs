using AgenStart.Core.Catalogue;
using AgenStart.Core.Profiles;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels;

public sealed class ProfileOptionViewModel : LocalizedRow
{
    private bool _isSelected;

    public ProfileOptionViewModel(ProfileDefinition profile, UserProfile flag)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        Flag = flag;
        Id = profile.Id.Value;
    }

    public ProfileDefinition Profile { get; }

    public UserProfile Flag { get; }

    public string Id { get; }

    public string Label => Profile.Label.Get(Text.Language);

    public string Description => Profile.Description?.Get(Text.Language) ?? string.Empty;

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
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}
