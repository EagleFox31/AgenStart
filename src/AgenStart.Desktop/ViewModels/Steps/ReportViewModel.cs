using System.Collections.ObjectModel;
using System.Globalization;
using AgenStart.Application.Installation;
using AgenStart.Catalogue;
using AgenStart.PackageManagement;

namespace AgenStart.Desktop.ViewModels.Steps;

public sealed class ReportViewModel : StepViewModelBase
{
    private InstallationReport? _report;
    private bool _hasReport;
    private string _reportStatus = "No installation report yet.";
    private string _reportCompletedAt = "—";

    public ObservableCollection<ReportRowViewModel> ReportItems { get; } = [];

    public bool HasReport
    {
        get => _hasReport;
        private set => SetField(ref _hasReport, value);
    }

    public string ReportStatus
    {
        get => _reportStatus;
        private set => SetField(ref _reportStatus, value);
    }

    public string ReportCompletedAt
    {
        get => _reportCompletedAt;
        private set => SetField(ref _reportCompletedAt, value);
    }

    public int ReportProcessedCount => ReportItems.Count;
    public int ReportInstalledCount => _report?.Items.Count(item =>
        item.State == InstallationQueueItemState.Succeeded &&
        item.LastOperationStatus != PackageOperationStatus.AlreadyInstalled) ?? 0;
    public int ReportAlreadyInstalledCount => ReportItems.Count(row => row.Result == "Already installed");
    public int ReportFailedCount => _report?.FailedCount ?? 0;
    public bool ReportRequiresReboot => _report?.Items.Any(item => item.RequiresReboot) == true;

    public void Reset()
    {
        _report = null;
        ReportItems.Clear();
        HasReport = false;
        ReportStatus = "No installation report yet.";
        ReportCompletedAt = "—";
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
        ReportStatus = report.State == InstallationSessionState.Cancelled
            ? "The approved setup was cancelled before every item completed."
            : report.FailedCount > 0
                ? "AgenStart finished the approved setup with issues."
                : "AgenStart finished the approved setup.";
        ReportCompletedAt = report.CompletedAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";
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

            var result = item.State switch
            {
                InstallationQueueItemState.Succeeded when item.LastOperationStatus == PackageOperationStatus.AlreadyInstalled => "Already installed",
                InstallationQueueItemState.Succeeded => "Installed",
                InstallationQueueItemState.Failed => "Failed",
                InstallationQueueItemState.Skipped => "Skipped",
                InstallationQueueItemState.Cancelled => "Cancelled",
                _ => item.State.ToString()
            };

            ReportItems.Add(new ReportRowViewModel(
                item.ApplicationId,
                application.Name,
                BuildInitials(application.Name),
                result,
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
                "Already installed",
                null,
                requiresReboot: false));
        }
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(ReportProcessedCount));
        OnPropertyChanged(nameof(ReportInstalledCount));
        OnPropertyChanged(nameof(ReportAlreadyInstalledCount));
        OnPropertyChanged(nameof(ReportFailedCount));
        OnPropertyChanged(nameof(ReportRequiresReboot));
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
