using AgenStart.Core.Catalogue;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class UsageProfileViewModel : StepViewModelBase
{
    private UserProfile _selectedProfile = UserProfile.Development;

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

    public string SelectedProfileName => SelectedProfile.ToString();

    public string SelectedProfileDescription => SelectedProfile switch
    {
        UserProfile.Personal => "Everyday apps, browsing, communication and media.",
        UserProfile.Development => "Code, databases, terminals and developer tools.",
        UserProfile.Business => "Office, communication, productivity and collaboration.",
        UserProfile.Creation => "Design, media, content and creative workflows.",
        UserProfile.Training => "Learning, course tools and guided study setups.",
        _ => string.Empty
    };

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
