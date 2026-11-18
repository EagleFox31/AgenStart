using System.Collections.ObjectModel;
using System.ComponentModel;
using AgenStart.Desktop.Services;
using AgenStart.Uninstall;

namespace AgenStart.Desktop.ViewModels.Steps;

/// <summary>
/// A prepared uninstall plan, shown for review. The checkboxes only change which items the
/// in-memory selection (and the export) contains: nothing here can uninstall, delete or launch.
/// </summary>
public sealed class UninstallPlanViewModel : INotifyPropertyChanged
{
    private readonly ILocalizer _text;

    public UninstallPlanViewModel(UninstallPlan plan, ILocalizer text)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _text = text ?? throw new ArgumentNullException(nameof(text));

        // Low-confidence items are never offered in the page: they only appear in the export (ADR-0006).
        Leftovers = new(plan.Leftovers
            .Where(item => item.Confidence.Level is not ConfidenceLevel.Low)
            .Select(item => new UninstallPlanItemViewModel(item, text, OnSelectionChanged)));
        ProtectedItems = new(plan.ProtectedUserData
            .Select(item => new UninstallPlanItemViewModel(item, text, OnSelectionChanged)));
        LowConfidenceCount = plan.Leftovers.Count(item => item.Confidence.Level is ConfidenceLevel.Low);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UninstallPlan Plan { get; }

    public ObservableCollection<UninstallPlanItemViewModel> Leftovers { get; }

    public ObservableCollection<UninstallPlanItemViewModel> ProtectedItems { get; }

    public int LowConfidenceCount { get; }

    public string StrategyText => _text[Plan.Official.Strategy switch
    {
        OfficialStrategy.WinGet => "Uninstall.Plan.Strategy.WinGet",
        OfficialStrategy.Msi => "Uninstall.Plan.Strategy.Msi",
        OfficialStrategy.QuietUninstallString => "Uninstall.Plan.Strategy.Quiet",
        OfficialStrategy.InstallerSilentFlags => "Uninstall.Plan.Strategy.SilentFlags",
        OfficialStrategy.Interactive => "Uninstall.Plan.Strategy.Interactive",
        _ => "Uninstall.Plan.Strategy.Unavailable"
    }];

    /// <summary>The command the vendor's uninstaller would run, as text only.</summary>
    public string CommandPreview => Plan.Official.CommandPreview ?? string.Empty;

    public bool HasCommandPreview => !string.IsNullOrWhiteSpace(Plan.Official.CommandPreview);

    public bool OfficialRequiresElevation => Plan.Official.RequiresElevation;

    public bool HasLeftovers => Leftovers.Count > 0;

    public bool HasProtected => ProtectedItems.Count > 0;

    public bool HasLowConfidence => LowConfidenceCount > 0;

    public string LowConfidenceText => _text.Plural("Uninstall.Plan.LowConfidence", LowConfidenceCount);

    public bool HasSkipped => Plan.Skipped.Count > 0;

    public string SkippedText => _text.Plural("Uninstall.Plan.Skipped", Plan.Skipped.Count);

    public bool HasRunning => Plan.Processes.Count > 0;

    public string RunningText => _text.Plural(
        "Uninstall.Plan.Running",
        Plan.Processes.Count,
        string.Join(", ", Plan.Processes.Select(process => process.Name).Distinct(StringComparer.OrdinalIgnoreCase)));

    public bool RulesInvalid => Plan.Diagnostics.Any(diagnostic => diagnostic.Code == UninstallPlanService.RulesInvalid);

    /// <summary>True when at least one protected user-data item is checked: the page then warns.</summary>
    public bool HasProtectedSelected => ProtectedItems.Any(item => item.IsSelected);

    public IReadOnlySet<string> SelectedItemIds => Leftovers.Concat(ProtectedItems)
        .Where(item => item.IsSelected)
        .Select(item => item.Item.ItemId)
        .ToHashSet(StringComparer.Ordinal);

    public PlanSelectionSummary Selection => UninstallSelection.Summarize(Plan, SelectedItemIds);

    public string SelectionText
    {
        get
        {
            var selection = Selection;
            return _text.Plural("Uninstall.Plan.Selection", selection.ItemCount, UiUnits.Bytes(selection.TotalBytes).Resolve(_text));
        }
    }

    public bool ExceedsVolumeThresholds => Selection.ExceedsVolumeThresholds;

    /// <summary>The plan as exported: the current selection, marked as not executed.</summary>
    public string ToJson(DateTimeOffset exportedAt) => UninstallPlanJsonWriter.Write(Plan, SelectedItemIds, exportedAt);

    public void RefreshText()
    {
        foreach (var item in Leftovers.Concat(ProtectedItems))
        {
            item.RefreshText();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void OnSelectionChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProtectedSelected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedItemIds)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selection)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExceedsVolumeThresholds)));
    }
}

/// <summary>One proposed leftover with its checkbox; texts follow the current language.</summary>
public sealed class UninstallPlanItemViewModel : INotifyPropertyChanged
{
    private readonly ILocalizer _text;
    private readonly Action _selectionChanged;
    private bool _isSelected;

    public UninstallPlanItemViewModel(PlanItem item, ILocalizer text, Action selectionChanged)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
        _isSelected = item.DefaultSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlanItem Item { get; }

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
            _selectionChanged();
        }
    }

    public string Target => Item.Target;

    public string KindText => _text[Item.Kind switch
    {
        LeftoverKind.Directory => "Uninstall.Plan.Kind.Folder",
        LeftoverKind.File => "Uninstall.Plan.Kind.File",
        LeftoverKind.Shortcut => "Uninstall.Plan.Kind.Shortcut",
        LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => "Uninstall.Plan.Kind.Registry",
        _ => "Uninstall.Plan.Kind.Generic"
    }];

    public string ClassText => _text[Item.DataClass switch
    {
        DataClass.AppData => "Uninstall.Plan.Class.AppData",
        DataClass.Cache => "Uninstall.Plan.Class.Cache",
        DataClass.Configuration => "Uninstall.Plan.Class.Configuration",
        DataClass.UserData => "Uninstall.Plan.Class.UserData",
        _ => "Uninstall.Plan.Class.Shortcut"
    }];

    public string ConfidenceText => _text.Format(
        Item.Confidence.Level switch
        {
            ConfidenceLevel.High => "Uninstall.Plan.Confidence.High",
            ConfidenceLevel.Medium => "Uninstall.Plan.Confidence.Medium",
            _ => "Uninstall.Plan.Confidence.Low"
        },
        Item.Confidence.Score);

    public string SizeText => Item.SizeBytes is { } bytes
        ? Item.SizeIsPartial
            ? _text.Format("Uninstall.Plan.SizeAtLeast", UiUnits.Bytes(bytes).Resolve(_text))
            : UiUnits.Bytes(bytes).Resolve(_text)
        : _text["Common.Unknown"];

    public bool RequiresElevation => Item.RequiresElevation;

    public void RefreshText() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
