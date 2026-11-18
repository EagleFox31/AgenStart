using AgenStart.Core.Catalogue;
using AgenStart.Core.Profiles;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class UsageProfileViewModel : StepViewModelBase
{
    private readonly ProfileRegistry _profiles;
    private UserProfile _selectedProfile = UserProfile.Development;

    public UsageProfileViewModel(ProfileRegistry? profiles = null, ILocalizer? text = null)
        : base(text)
    {
        _profiles = profiles ?? ProfileRegistry.Default;
    }

    public UserProfile SelectedProfile
    {
        get => _selectedProfile;
        private set
        {
            if (!SetField(ref _selectedProfile, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(SelectedProfileDescription));
        }
    }

    /// <summary>Profile labels come from the profile registry (en/fr), not from the UI dictionaries.</summary>
    public string SelectedProfileName => SelectedProfileNameText.Resolve(Text);

    /// <summary>The selected profile names, re-resolved in the current language each time they are read.</summary>
    public UiText SelectedProfileNameText
    {
        get
        {
            var selected = _profiles.Expand(SelectedProfile);
            return UiText.Dynamic(text => selected.Count == 0
                ? SelectedProfile.ToString()
                : string.Join(" + ", selected.Select(profile => profile.Label.Get(text.Language))));
        }
    }

    public string SelectedProfileDescription
    {
        get
        {
            var selected = _profiles.Expand(SelectedProfile);
            return selected.Count == 1
                ? selected[0].Description?.Get(Text.Language) ?? string.Empty
                : string.Empty;
        }
    }

    public bool TrySelect(UserProfile profile)
    {
        if (SelectedProfile == profile)
        {
            return false;
        }

        SelectedProfile = profile;
        return true;
    }
}
