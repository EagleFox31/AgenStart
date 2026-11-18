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
/// Read-only list of the programs installed on this PC, with a reviewable uninstall plan for the
/// selected one. By default it only depends on read ports and on the user-started plan export:
/// nothing here can uninstall, delete, launch or change anything on the PC. Only when the experimental
/// setting was on at startup does it also hold the official uninstall runner, used after a confirmation.
/// </summary>
public sealed class UninstallViewModel : StepViewModelBase
{
    private readonly IInstalledProgramInventory _inventory;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUninstallPlanSource? _planSource;
    private readonly IUninstallPlanExportTarget? _exportTarget;
    private readonly IOfficialUninstallRunner? _officialUninstall;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _preparePlanCommand;
    private readonly RelayCommand _exportPlanCommand;
    private CancellationTokenSource? _planning;
    private UninstallPlanViewModel? _currentPlan;
    private OfficialUninstallViewModel? _official;
    private bool _isUninstalling;
    private bool _isPlanning;
    private bool _isExporting;
    private UiText _planNotice = UiText.Empty;
    private IReadOnlyList<UninstallProgramRowViewModel> _all = [];
    private string _searchText = string.Empty;
    private UninstallSortOrder _sortOrder = UninstallSortOrder.Name;
    private UninstallProgramRowViewModel? _selectedProgram;
    private Task? _load;
    private bool _isLoading;
    private bool _hasLoaded;
    private UiText _notice = UiText.Empty;

    public UninstallViewModel(
        IInstalledProgramInventory inventory,
        IUiDispatcher dispatcher,
        ILocalizer? text = null,
        IUninstallPlanSource? planSource = null,
        IUninstallPlanExportTarget? exportTarget = null,
        IOfficialUninstallRunner? officialUninstall = null)
        : base(text)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _planSource = planSource;
        _exportTarget = exportTarget;
        _officialUninstall = officialUninstall;
        _refreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsLoading && !IsUninstalling);
        _preparePlanCommand = new RelayCommand(() => _ = PreparePlanAsync(), () => CanPreparePlan);
        _exportPlanCommand = new RelayCommand(() => _ = ExportPlanAsync(), () => CanExportPlan);
        SortByNameCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Name);
        SortByPublisherCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Publisher);
        SortBySizeCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Size);
        SortByDateCommand = new RelayCommand(() => SortOrder = UninstallSortOrder.Date);
    }

    /// <summary>Programs matching the search, in the chosen order.</summary>
    public ObservableCollection<UninstallProgramRowViewModel> Programs { get; } = [];

    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>Reads the machine to describe what an uninstall would do. It never acts.</summary>
    public ICommand PreparePlanCommand => _preparePlanCommand;

    /// <summary>Saves the plan as JSON to a file the user picks: the page's only write.</summary>
    public ICommand ExportPlanCommand => _exportPlanCommand;
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
            if (IsUninstalling)
            {
                // The program being uninstalled stays selected until its session has finished.
                OnPropertyChanged(nameof(SelectedProgram));
                return;
            }

            if (SetField(ref _selectedProgram, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                ClearPlan();
            }
        }
    }

    public bool HasSelection => _selectedProgram is not null;

    public UninstallPlanViewModel? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (SetField(ref _currentPlan, value))
            {
                OnPropertyChanged(nameof(HasPlan));
                OnPropertyChanged(nameof(CanExportPlan));
                _exportPlanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPlan => _currentPlan is not null;

    /// <summary>True only when the experimental official uninstall was composed at startup.</summary>
    public bool IsOfficialUninstallEnabled => _officialUninstall is not null;

    /// <summary>The confirmation, progress and report for the current plan; null when disabled.</summary>
    public OfficialUninstallViewModel? OfficialUninstall
    {
        get => _official;
        private set => SetField(ref _official, value);
    }

    /// <summary>While a session runs, the selection, the list and planning are locked.</summary>
    public bool IsUninstalling
    {
        get => _isUninstalling;
        private set
        {
            if (SetField(ref _isUninstalling, value))
            {
                OnPropertyChanged(nameof(CanBrowse));
                OnPropertyChanged(nameof(CanPreparePlan));
                _preparePlanCommand.RaiseCanExecuteChanged();
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanBrowse => !IsUninstalling;

    public bool IsPlanning
    {
        get => _isPlanning;
        private set
        {
            if (SetField(ref _isPlanning, value))
            {
                OnPropertyChanged(nameof(CanPreparePlan));
                _preparePlanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetField(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExportPlan));
                _exportPlanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanPreparePlan => _planSource is not null && HasSelection && !IsPlanning && !IsUninstalling;

    public bool CanExportPlan => _exportTarget is not null && HasPlan && !IsExporting;

    public string PlanNotice => _planNotice.Resolve(Text);

    public bool HasPlanNotice => !ReferenceEquals(_planNotice, UiText.Empty);

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

    public Task RefreshAsync() => (IsLoading || IsUninstalling) && _load is not null ? _load : _load = LoadAsync();

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

    public async Task PreparePlanAsync()
    {
        var row = _selectedProgram;
        if (_planSource is null || row is null || IsPlanning || IsUninstalling)
        {
            return;
        }

        var planning = new CancellationTokenSource();
        _planning = planning;
        IsPlanning = true;
        SetPlanNotice(UiText.Empty);
        var programs = _all.Select(candidate => candidate.Program).ToArray();

        UninstallPlan? plan;
        try
        {
            plan = await _planSource.CreatePlanAsync(row.Program, programs, planning.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Planning is best effort: a failure is shown as a message, never as a crash.
            plan = null;
        }

        _dispatcher.Post(() => ApplyPlan(planning, row.Program.Id, plan));
    }

    public async Task ExportPlanAsync()
    {
        var plan = _currentPlan;
        if (_exportTarget is null || plan is null || IsExporting)
        {
            return;
        }

        IsExporting = true;
        SetPlanNotice(UiText.Empty);
        UiText notice;
        try
        {
            var saved = await _exportTarget.SaveAsync(ExportFileName(plan.Plan.ProgramName), plan.ToJson(DateTimeOffset.UtcNow));
            notice = saved ? UiText.Key("Uninstall.Plan.Exported") : UiText.Empty;
        }
        catch (Exception)
        {
            notice = UiText.Key("Uninstall.Plan.ExportFailed");
        }

        _dispatcher.Post(() =>
        {
            IsExporting = false;
            SetPlanNotice(notice);
        });
    }

    /// <summary>A file name built only from the ASCII letters and digits of the program name.</summary>
    public static string ExportFileName(string? programName)
    {
        var slug = new System.Text.StringBuilder();
        foreach (var character in (programName ?? string.Empty).ToLowerInvariant())
        {
            if (slug.Length >= 48)
            {
                break;
            }

            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var name = slug.ToString().Trim('-');
        return $"agenstart-uninstall-plan-{(name.Length == 0 ? "program" : name)}.json";
    }

    private void ApplyPlan(CancellationTokenSource planning, string programId, UninstallPlan? plan)
    {
        planning.Dispose();
        if (!ReferenceEquals(_planning, planning))
        {
            // The selection changed while planning: ClearPlan already reset the page.
            return;
        }

        _planning = null;
        IsPlanning = false;
        if (_selectedProgram?.Program.Id != programId)
        {
            return;
        }

        CurrentPlan = plan is null ? null : new UninstallPlanViewModel(plan, Text);
        OfficialUninstall = CurrentPlan is not null && _officialUninstall is not null && _selectedProgram is { } selected
            ? new OfficialUninstallViewModel(CurrentPlan, selected.Program, _officialUninstall, _dispatcher, Text, running => IsUninstalling = running)
            : null;
        SetPlanNotice(CurrentPlan switch
        {
            null => UiText.Key("Uninstall.Plan.Failed"),
            { RulesInvalid: true } => UiText.Key("Uninstall.Plan.RulesInvalid"),
            _ => UiText.Empty
        });
    }

    private void ClearPlan()
    {
        if (_planning is { } planning)
        {
            _planning = null;
            planning.Cancel();
            IsPlanning = false;
        }

        CurrentPlan = null;
        OfficialUninstall = null;
        SetPlanNotice(UiText.Empty);
        OnPropertyChanged(nameof(CanPreparePlan));
        _preparePlanCommand.RaiseCanExecuteChanged();
    }

    private void SetPlanNotice(UiText notice)
    {
        _planNotice = notice;
        OnPropertyChanged(nameof(PlanNotice));
        OnPropertyChanged(nameof(HasPlanNotice));
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

        _currentPlan?.RefreshText();
        _official?.RefreshText();
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
