using Avalonia.Media;

namespace AgenStart.Desktop.ViewModels;

public interface IAppRowLogo
{
    string Initials { get; }
    IImage? IconSource { get; }
    bool HasLogo { get; }
    bool ShowInitials { get; }
}
