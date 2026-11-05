using System.ComponentModel;
using System.Runtime.CompilerServices;
using AgenStart.Desktop.Services;

namespace AgenStart.Desktop.ViewModels.Steps;

public abstract class StepViewModelBase : INotifyPropertyChanged
{
    protected StepViewModelBase(ILocalizer? text = null)
    {
        // Step view models live as long as the window, so the subscription never needs removing.
        Text = text ?? AvaloniaLocalizationService.Shared;
        Text.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected ILocalizer Text { get; }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void SetText(ref UiText field, UiText value, string propertyName)
    {
        field = value;
        OnPropertyChanged(propertyName);
    }

    /// <summary>Re-reads every binding: string properties resolve their keys in the new language.</summary>
    protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
