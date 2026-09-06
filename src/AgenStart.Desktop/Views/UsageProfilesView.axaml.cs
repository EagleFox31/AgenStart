using Avalonia.Controls;
using Avalonia.Interactivity;
using AgenStart.Core.Catalogue;

namespace AgenStart.Desktop.Views;

public sealed partial class UsageProfilesView : UserControl
{
    private bool _updating;

    public UsageProfilesView()
    {
        InitializeComponent();
        SetSelection(UserProfile.Development);
    }

    public event Action<UserProfile>? SelectionChanged;
    public event EventHandler? BuildRequested;
    public event EventHandler? BackRequested;

    public UserProfile SelectedProfiles => BuildSelection();

    public void SetSelection(UserProfile profiles)
    {
        _updating = true;
        try
        {
            PersonalCheckBox.IsChecked = profiles.HasFlag(UserProfile.Personal);
            BusinessCheckBox.IsChecked = profiles.HasFlag(UserProfile.Business);
            LearningCheckBox.IsChecked = profiles.HasFlag(UserProfile.Learning);
            DevelopmentCheckBox.IsChecked = profiles.HasFlag(UserProfile.Development);
            CreativeCheckBox.IsChecked = profiles.HasFlag(UserProfile.Creative);
            GamingCheckBox.IsChecked = profiles.HasFlag(UserProfile.Gaming);

            if (BuildSelection() == UserProfile.None)
            {
                DevelopmentCheckBox.IsChecked = true;
            }

            RefreshSummary();
        }
        finally
        {
            _updating = false;
        }
    }

    private void ProfileCheckBox_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var selected = BuildSelection();
        if (selected == UserProfile.None)
        {
            _updating = true;
            try
            {
                if (sender is CheckBox checkBox)
                {
                    checkBox.IsChecked = true;
                }
            }
            finally
            {
                _updating = false;
            }

            selected = BuildSelection();
        }

        RefreshSummary();
        SelectionChanged?.Invoke(selected);
    }

    private void BuildButton_OnClick(object? sender, RoutedEventArgs e) =>
        BuildRequested?.Invoke(this, EventArgs.Empty);

    private void BackButton_OnClick(object? sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private UserProfile BuildSelection()
    {
        var result = UserProfile.None;
        if (PersonalCheckBox.IsChecked == true) result |= UserProfile.Personal;
        if (BusinessCheckBox.IsChecked == true) result |= UserProfile.Business;
        if (LearningCheckBox.IsChecked == true) result |= UserProfile.Learning;
        if (DevelopmentCheckBox.IsChecked == true) result |= UserProfile.Development;
        if (CreativeCheckBox.IsChecked == true) result |= UserProfile.Creative;
        if (GamingCheckBox.IsChecked == true) result |= UserProfile.Gaming;
        return result;
    }

    private void RefreshSummary()
    {
        var selected = new List<string>();
        if (PersonalCheckBox.IsChecked == true) selected.Add("Personal");
        if (BusinessCheckBox.IsChecked == true) selected.Add("Work");
        if (LearningCheckBox.IsChecked == true) selected.Add("Learning");
        if (DevelopmentCheckBox.IsChecked == true) selected.Add("Development");
        if (CreativeCheckBox.IsChecked == true) selected.Add("Creative");
        if (GamingCheckBox.IsChecked == true) selected.Add("Gaming");

        SelectedProfilesText.Text = string.Join(" + ", selected);
    }
}
