using System.Collections.ObjectModel;
using AgenStart.Application.Installation;
using AgenStart.Catalogue;
using AgenStart.Desktop.Services;
using AgenStart.PackageManagement;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class ReportViewModel : StepViewModelBase
{
    private InstallationReport? _report;
    private bool _hasReport;
    private UiText _reportStatus = UiText.Key("Report.Status.None");

    public ObservableCollection<ReportRowViewModel> ReportItems { get; } = [];

    public bool HasReport
    {
        get => _hasReport;
        private set => SetField(ref _hasReport, value);
    }

    public string ReportStatus => _reportStatus.Resolve(Text);

    public string ReportCompletedAt =>
        _report?.CompletedAtUtc?.ToLocalTime().ToString("g", Text.Culture) ?? "—";

    public int ReportProcessedCount => ReportItems.Count;
    public int ReportInstalledCount => _report?.Items.Count(item =>
        item.State == InstallationQueueItemState.Succeeded &&
        item.LastOperationStatus != PackageOperationStatus.AlreadyInstalled) ?? 0;
    public int ReportAlreadyInstalledCount => ReportItems.Count(row => row.IsAlreadyInstalled);
    public int ReportFailedCount => _report?.FailedCount ?? 0;
    public bool ReportRequiresReboot => _report?.Items.Any(item => item.RequiresReboot) == true;
    public string ReportTitle => Text[_report switch
    {
        { State: InstallationSessionState.Cancelled } => "Report.Title.Cancelled",
        { FailedCount: > 0 } => "Report.Title.Issues",
        _ => "Report.Title.Ready"
    }];

    public void Reset()
    {
        _report = null;
        ReportItems.Clear();
        HasReport = false;
        SetText(ref _reportStatus, UiText.Key("Report.Status.None"), nameof(ReportStatus));
        OnPropertyChanged(nameof(ReportCompletedAt));
        RaiseSummary();
    }

    public void Apply(
        InstallationReport report,
        SoftwareCatalogue catalogue,
        IEnumerable<RecommendationRowViewModel> alreadyInstalledRecommendations)
    {
        _report = report;

        BuildRows(report, catalogue, alreadyInstalledRecommendations);
        HasReport = true;
        SetText(ref _reportStatus, UiText.Key(report.State == InstallationSessionState.Cancelled
            ? "Report.Status.Cancelled"
            : report.FailedCount > 0
                ? "Report.Status.Issues"
                : "Report.Status.Finished"), nameof(ReportStatus));
        OnPropertyChanged(nameof(ReportCompletedAt));
        RaiseSummary();
    }

    private void BuildRows(
        InstallationReport report,
        SoftwareCatalogue catalogue,
        IEnumerable<RecommendationRowViewModel> alreadyInstalledRecommendations)
    {
        ReportItems.Clear();
        var catalogueById = catalogue.Applications.ToDictionary(
            application => application.Id,
            StringComparer.OrdinalIgnoreCase);
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in report.Items.OrderBy(item => item.Sequence))
        {
            if (!catalogueById.TryGetValue(item.ApplicationId, out var application))
            {
                continue;
            }

            var alreadyInstalled = item.State == InstallationQueueItemState.Succeeded
                && item.LastOperationStatus == PackageOperationStatus.AlreadyInstalled;
            var result = item.State switch
            {
                _ when alreadyInstalled => UiText.Key("Report.Result.AlreadyInstalled"),
                InstallationQueueItemState.Succeeded => UiText.Key("Report.Result.Installed"),
                InstallationQueueItemState.Failed => UiText.Key("Report.Result.Failed"),
                InstallationQueueItemState.Skipped => UiText.Key("Report.Result.Skipped"),
                InstallationQueueItemState.Cancelled => UiText.Key("Report.Result.Cancelled"),
                _ => UiText.Raw(item.State.ToString())
            };

            ReportItems.Add(new ReportRowViewModel(
                item.ApplicationId,
                application.Name,
                BuildInitials(application.Name),
                result,
                alreadyInstalled,
                item.InstalledVersion,
                item.RequiresReboot));
            included.Add(item.ApplicationId);
        }

        foreach (var recommendation in alreadyInstalledRecommendations.Where(row =>
                     !included.Contains(row.ApplicationId)))
        {
            ReportItems.Add(new ReportRowViewModel(
                recommendation.ApplicationId,
                recommendation.Name,
                recommendation.Initials,
                UiText.Key("Report.Result.AlreadyInstalled"),
                alreadyInstalled: true,
                null,
                requiresReboot: false));
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in ReportItems)
        {
            row.RefreshText();
        }
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ReportProcessedCount));
        OnPropertyChanged(nameof(ReportInstalledCount));
        OnPropertyChanged(nameof(ReportAlreadyInstalledCount));
        OnPropertyChanged(nameof(ReportFailedCount));
        OnPropertyChanged(nameof(ReportRequiresReboot));
        OnPropertyChanged(nameof(ReportTitle));
    }

    private static string BuildInitials(string name)
    {
        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        return words.Length switch
        {
            0 => "•",
            1 => words[0][..1].ToUpperInvariant(),
            _ => string.Concat(words.Select(word => char.ToUpperInvariant(word[0])))
        };
    }
}
