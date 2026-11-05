using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels;

/// <summary>What the shell knows about the setup flow when it decides which tabs can be opened.</summary>
public readonly record struct NavigationState(
    bool HasSnapshot,
    bool HasRecommendations,
    bool HasReview,
    bool HasInstallation,
    bool HasReport);

public static class NavigationGate
{
    /// <summary>A step can be opened once the data it shows exists; the current step is handled by the caller.</summary>
    public static bool IsAvailable(AppStep step, NavigationState state) => step switch
    {
        AppStep.Overview or AppStep.History or AppStep.Settings => true,
        AppStep.YourPc or AppStep.UsageProfile => state.HasSnapshot,
        AppStep.Recommendations => state.HasRecommendations,
        AppStep.Review => state.HasReview,
        AppStep.Installation => state.HasInstallation || state.HasReport,
        AppStep.Report => state.HasReport,
        _ => false
    };
}

/// <summary>One header tab. Its command is disabled while the step cannot be opened.</summary>
public sealed class NavigationTabViewModel : INotifyPropertyChanged
{
    private readonly ILocalizer _text;
    private bool _isActive;
    private bool _isEnabled;

    public NavigationTabViewModel(AppStep step, string labelKey, string icon, ILocalizer text, Action<AppStep> select)
    {
        Step = step;
        LabelKey = labelKey;
        Icon = icon;
        _text = text;
        SelectCommand = new RelayCommand(() => select(Step), () => IsEnabled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppStep Step { get; }

    public string LabelKey { get; }

    public string Icon { get; }

    public string Label => _text[LabelKey];

    public RelayCommand SelectCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (SetField(ref _isEnabled, value))
            {
                SelectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    internal void Update(bool isActive, bool isEnabled)
    {
        IsActive = isActive;
        IsEnabled = isEnabled;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(Label));

    private bool SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
