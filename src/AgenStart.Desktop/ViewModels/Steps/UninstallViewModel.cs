using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using AgenStart.Desktop.Services;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.ViewModels.Steps;

public enum UninstallSortOrder
{
    Name,
    Publisher,
    Size,
    Date
}

/// <summary>
/// Read-only list of the programs installed on this PC. It only depends on the inventory read
/// port: nothing here can uninstall, delete or write anything.
/// </summary>
public sealed class UninstallViewModel : StepViewModelBase
{
    private readonly IInstalledProgramInventory _inventory;
    private readonly IUiDispatcher _dispatcher;
    private readonly RelayCommand _refreshCommand;
    private IReadOnlyList<UninstallProgramRowViewModel> _all = [];
    private string _searchText = string.Empty;
    private UninstallSortOrder _sortOrder = UninstallSortOrder.Name;
    private UninstallProgramRowViewModel? _selectedProgram;
    private Task? _load;
    private bool _isLoading;
    private bool _hasLoaded;
    private UiText _notice = UiText.Empty;

    public UninstallViewModel(IInstalledProgramInventory inventory, IUiDispatcher dispatcher, ILocalizer? text = null)
        : base(text)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _refreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsLoading);
        SortByNameCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Name);
        SortByPublisherCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Publisher);
        SortBySizeCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Size);
        SortByDateCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Date);
    }

    /// <summary>Programs matching the search, in the chosen order.</summary>
    public ObservableCollection<UninstallProgramRowViewModel> Programs { get; } = [];

    public ICommand RefreshCommand => _refreshCommand;
    public ICommand SortByNameCommand { get; }
    public ICommand SortByPublisherCommand { get; }
    public ICommand SortBySizeCommand { get; }
    public ICommand SortByDateCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                ApplyView();
            }
        }
    }

    public UninstallSortOrder SortOrder
    {
        get => _sortOrder;
        set
        {
            if (SetField(ref _sortOrder, value))
            {
                OnPropertyChanged(nameof(IsSortByName));
                OnPropertyChanged(nameof(IsSortByPublisher));
                OnPropertyChanged(nameof(IsSortBySize));
                OnPropertyChanged(nameof(IsSortByDate));
                ApplyView();
            }
        }
    }

    public bool IsSortByName => _sortOrder == UninstallSortOrder.Name;
    public bool IsSortByPublisher => _sortOrder == UninstallSortOrder.Publisher;
    public bool IsSortBySize => _sortOrder == UninstallSortOrder.Size;
    public bool IsSortByDate => _sortOrder == UninstallSortOrder.Date;

    public UninstallProgramRowViewModel? SelectedProgram
    {
        get => _selectedProgram;
        set
        {
            if (SetField(ref _selectedProgram, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => _selectedProgram is not null;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowNoMatch));
                OnPropertyChanged(nameof(ShowList));
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Visible programs.</summary>
    public int ProgramCount => Programs.Count;

    /// <summary>Every listed program, whatever the search.</summary>
    public int TotalCount => _all.Count;

    /// <summary>Sum of the sizes Windows reports for the visible programs; unknown sizes count as zero.</summary>
    public long TotalSizeBytes => Programs.Sum(program => program.Program.EstimatedSizeBytes ?? 0);

    public string HeaderText => _hasLoaded
        ? UiText.Plural("Uninstall.Summary", ProgramCount, UiUnits.Bytes(TotalSizeBytes)).Resolve(Text)
        : string.Empty;

    public bool ShowList => !IsLoading && ProgramCount > 0;

    public bool ShowNoMatch => _hasLoaded && !IsLoading && ProgramCount == 0;

    public string NoMatchText => (TotalCount == 0 ? UiText.Key("Uninstall.NoPrograms") : UiText.Key("Uninstall.NoMatch")).Resolve(Text);

    public string Notice => _notice.Resolve(Text);

    public bool HasNotice => !ReferenceEquals(_notice, UiText.Empty);

    /// <summary>Starts the first read in the background; later calls reuse it. Never blocks the caller.</summary>
    public void EnsureLoaded() => _ = EnsureLoadedAsync();

    public Task EnsureLoadedAsync() => _load ??= LoadAsync();

    public Task RefreshAsync() => IsLoading && _load is not null ? _load : _load = LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;

        InstalledProgramInventoryResult? result = null;
        try
        {
            result = await _inventory.CaptureAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Reading is best effort: any failure is shown as a message, never as a crash.
            result = null;
        }

        _dispatcher.Post(() => Apply(result));
    }

    private void Apply(InstalledProgramInventoryResult? result)
    {
        var programs = result?.Programs ?? [];
        _all = programs.Select(program => new UninstallProgramRowViewModel(program, Text)).ToArray();
        _hasLoaded = true;
        _notice = result?.Status switch
        {
            null => UiText.Key("Uninstall.Notice.Failed"),
            InstalledProgramInventoryStatus.Unavailable => UiText.Key("Uninstall.Notice.Unavailable"),
            InstalledProgramInventoryStatus.Partial => UiText.Key("Uninstall.Notice.Partial"),
            _ => UiText.Empty
        };

        ApplyView();
        IsLoading = false;
        OnPropertyChanged(nameof(Notice));
        OnPropertyChanged(nameof(HasNotice));
        OnPropertyChanged(nameof(TotalCount));
    }

    private void ApplyView()
    {
        var selectedId = _selectedProgram?.Program.Id;
        var query = ProgramNameNormalizer.Normalize(_searchText);
        var visible = Order(_all.Where(row => row.Matches(query)), _sortOrder).ToArray();

        Programs.Clear();
        foreach (var row in visible)
        {
            Programs.Add(row);
        }

        SelectedProgram = selectedId is null
            ? null
            : visible.FirstOrDefault(row => row.Program.Id == selectedId);

        OnPropertyChanged(nameof(ProgramCount));
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(NoMatchText));
    }

    private static IEnumerable<UninstallProgramRowViewModel> Order(
        IEnumerable<UninstallProgramRowViewModel> rows,
        UninstallSortOrder order)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        var ordered = order switch
        {
            UninstallSortOrder.Publisher => rows
                .OrderBy(row => row.Program.Publisher is null)
                .ThenBy(row => row.Program.Publisher, comparer)
                .ThenBy(row => row.Program.DisplayName, comparer),
            UninstallSortOrder.Size => rows
                .OrderBy(row => row.Program.EstimatedSizeBytes is null)
                .ThenByDescending(row => row.Program.EstimatedSizeBytes)
                .ThenBy(row => row.Program.DisplayName, comparer),
            UninstallSortOrder.Date => rows
                .OrderBy(row => row.Program.InstallDate is null)
                .ThenByDescending(row => row.Program.InstallDate)
                .ThenBy(row => row.Program.DisplayName, comparer),
            _ => rows.OrderBy(row => row.Program.DisplayName, comparer)
        };

        return ordered.ThenBy(row => row.Program.Version, comparer).ThenBy(row => row.Program.Id, StringComparer.Ordinal);
    }

    protected override void OnLanguageChanged()
    {
        foreach (var row in _all)
        {
            row.RefreshText();
        }

        base.OnLanguageChanged();
    }
}

/// <summary>One installed program, with every text formatted for the current language.</summary>
public sealed class UninstallProgramRowViewModel : INotifyPropertyChanged
{
    private readonly ILocalizer _text;
    private readonly string _normalizedName;
    private readonly string _normalizedPublisher;

    public UninstallProgramRowViewModel(InstalledProgram program, ILocalizer text)
    {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _normalizedName = ProgramNameNormalizer.Normalize(program.DisplayName);
        _normalizedPublisher = ProgramNameNormalizer.Normalize(program.Publisher);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public InstalledProgram Program { get; }

    public string DisplayName => Program.DisplayName;

    public string Publisher => Program.Publisher ?? _text["Common.Unknown"];

    public string Version => Program.Version ?? _text["Common.Unknown"];

    public string InstallLocation => Program.InstallLocation ?? _text["Common.Unknown"];

    /// <summary>Publisher and version on one line under the name in the list.</summary>
    public string Caption => string.Join(" · ", new[] { Program.Publisher, Program.Version }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public string SizeText => Program.EstimatedSizeBytes is { } bytes
        ? UiUnits.Bytes(bytes).Resolve(_text)
        : _text["Common.Unknown"];

    public string InstallDateText => Program.InstallDate is { } date
        ? date.ToString("d", _text.Culture)
        : _text["Common.Unknown"];

    public string ScopeText => _text[Program.Scope switch
    {
        ProgramScope.Machine => "Uninstall.Scope.Machine",
        _ => "Uninstall.Scope.User"
    }];

    public string SourceText => _text[Program.Source switch
    {
        ProgramSource.WinGet => "Uninstall.Source.WinGet",
        ProgramSource.Msix => "Uninstall.Source.Msix",
        _ => "Uninstall.Source.Registry"
    }];

    public string InstallerKindText => _text[Program.InstallerKind switch
    {
        InstallerKind.Msi => "Uninstall.Installer.Msi",
        InstallerKind.Inno => "Uninstall.Installer.Inno",
        InstallerKind.Nsis => "Uninstall.Installer.Nsis",
        InstallerKind.Other => "Uninstall.Installer.Generic",
        _ => "Common.Unknown"
    }];

    public string SilentUninstallText => _text[UninstallCapabilities.IsSilentUninstallLikely(Program)
        ? "Uninstall.Silent.Likely"
        : "Uninstall.Silent.Unlikely"];

    internal bool Matches(string normalizedQuery) =>
        normalizedQuery.Length == 0
        || _normalizedName.Contains(normalizedQuery, StringComparison.Ordinal)
        || _normalizedPublisher.Contains(normalizedQuery, StringComparison.Ordinal);

    public void RefreshText() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
