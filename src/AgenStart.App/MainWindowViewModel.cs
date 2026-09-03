using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AgenStart.Application.GuidedSetup;
using AgenStart.Application.Installation;
using AgenStart.Core.Catalogue;
using AgenStart.Recommendations;
using Avalonia.Threading;

namespace AgenStart.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly GuidedSetupSession _session;
    private bool _disposed;
    private string _statusMessage = "Prêt. Rien ne sera installé sans ta confirmation.";

    public MainWindowViewModel(GuidedSetupSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.Changed += SessionChanged;
        _session.InstallationProgressChanged += InstallationProgressChanged;

        NextCommand = new RelayCommand(Continue, CanContinue);
        SelectPersonalCommand = new RelayCommand(() => SelectProfile(UserProfile.Personal));
        SelectDevelopmentCommand = new RelayCommand(() => SelectProfile(UserProfile.Development));
        SelectBusinessCommand = new RelayCommand(() => SelectProfile(UserProfile.Business));
        SelectCreationCommand = new RelayCommand(() => SelectProfile(UserProfile.Creation));
        SelectTrainingCommand = new RelayCommand(() => SelectProfile(UserProfile.Training));
        InstallCommand = new AsyncRelayCommand(ConfirmAndInstallAsync, () => IsConfirmation);
        CancelInstallationCommand = new RelayCommand(_session.CancelInstallation, () => IsInstallation);

        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RecommendationCardViewModel> Recommendations { get; } = [];
    public ObservableCollection<ReportItemViewModel> ReportItems { get; } = [];

    public ICommand NextCommand { get; }
    public ICommand SelectPersonalCommand { get; }
    public ICommand SelectDevelopmentCommand { get; }
    public ICommand SelectBusinessCommand { get; }
    public ICommand SelectCreationCommand { get; }
    public ICommand SelectTrainingCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand CancelInstallationCommand { get; }

    public bool IsWelcome => _session.Step == GuidedSetupStep.Welcome;
    public bool IsMachineSummary => _session.Step == GuidedSetupStep.MachineSummary;
    public bool IsProfileSelection => _session.Step == GuidedSetupStep.ProfileSelection;
    public bool IsRecommendations => _session.Step == GuidedSetupStep.Recommendations;
    public bool IsReview => _session.Step == GuidedSetupStep.Review;
    public bool IsConfirmation => _session.Step == GuidedSetupStep.Confirmation;
    public bool IsInstallation => _session.Step == GuidedSetupStep.Installation;
    public bool IsReport => _session.Step == GuidedSetupStep.Report;
    public bool ShowNextButton => IsWelcome || IsMachineSummary || IsRecommendations || IsReview;

    public string StepCounter => $"Étape {(int)_session.Step + 1} sur 8";

    public string StepTitle => _session.Step switch
    {
        GuidedSetupStep.Welcome => "Préparons ce PC sans le surcharger.",
        GuidedSetupStep.MachineSummary => "Voici ce qu’AgenStart comprend de la machine.",
        GuidedSetupStep.ProfileSelection => "À quoi va surtout servir ce PC ?",
        GuidedSetupStep.Recommendations => "Une sélection courte, avec une raison pour chaque choix.",
        GuidedSetupStep.Review => "Garde seulement ce que tu veux vraiment.",
        GuidedSetupStep.Confirmation => "Dernière vérification avant toute installation.",
        GuidedSetupStep.Installation => "Installation en cours, une application à la fois.",
        GuidedSetupStep.Report => "Configuration terminée.",
        _ => "AgenStart"
    };

    public string NextButtonText => _session.Step switch
    {
        GuidedSetupStep.Welcome => "Analyser ce PC",
        GuidedSetupStep.MachineSummary => "Choisir mon usage",
        GuidedSetupStep.Recommendations => "Revoir la sélection",
        GuidedSetupStep.Review => "Continuer vers la confirmation",
        _ => "Continuer"
    };

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string MachineOs =>
        $"{_session.Machine.Platform.Edition ?? "Windows"} · {_session.Machine.Platform.DisplayVersion ?? "version inconnue"} · {_session.Machine.Platform.Architecture}";

    public string MachineCpu =>
        $"{_session.Machine.Cpu.Model ?? "CPU inconnu"} · {_session.Machine.Cpu.LogicalProcessorCount} threads logiques";

    public string MachineMemory =>
        _session.Machine.Memory.TotalPhysicalBytes is ulong total
            ? $"{total / 1024d / 1024d / 1024d:0.#} Go de RAM"
            : "RAM non déterminée";

    public string MachineStorage =>
        _session.Machine.SystemDrive?.AvailableBytes is long free
            ? $"{free / 1024d / 1024d / 1024d:0.#} Go libres sur {_session.Machine.SystemDrive.Root}"
            : "Stockage système non déterminé";

    public string MachineGpu =>
        _session.Machine.Gpus.Count > 0
            ? _session.Machine.Gpus[0].Name ?? "GPU détecté"
            : "GPU non déterminé";

    public string PackageManager =>
        $"{_session.Machine.PackageManager.Kind} · {_session.Machine.PackageManager.State}";

    public string ProfileLabel => _session.Profile switch
    {
        UserProfile.Personal => "Personnel",
        UserProfile.Development => "Développement",
        UserProfile.Business => "Bureautique / Business",
        UserProfile.Creation => "Création",
        UserProfile.Training => "Formation",
        _ => "Non sélectionné"
    };

    public int SelectedCount => Recommendations.Count(static item => item.IsSelected);
    public string SelectionSummary => $"{SelectedCount} application(s) seront envoyées à la file d’installation après confirmation.";

    public int SucceededCount => _session.InstallationReport?.SucceededCount ?? 0;
    public int FailedCount => _session.InstallationReport?.FailedCount ?? 0;
    public int SkippedCount => _session.InstallationReport?.SkippedCount ?? 0;
    public int CancelledCount => _session.InstallationReport?.CancelledCount ?? 0;

    private void Continue() => _session.Continue();

    private bool CanContinue() => ShowNextButton;

    private void SelectProfile(UserProfile profile)
    {
        _session.SelectProfile(profile);
        StatusMessage = $"Profil {ProfileName(profile)} sélectionné. Les recommandations restent modifiables.";
    }

    private async Task ConfirmAndInstallAsync()
    {
        StatusMessage = "Confirmation reçue. Démarrage de la file d’installation…";
        try
        {
            await _session.ConfirmAndInstallAsync().ConfigureAwait(true);
            StatusMessage = _session.InstallationReport?.FailedCount > 0
                ? "Installation terminée avec des éléments à traiter."
                : "Installation terminée. Rapport prêt.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Installation annulée.";
        }
    }

    private async Task RetryAsync(string applicationId)
    {
        StatusMessage = $"Nouvelle vérification avant de réessayer {applicationId}…";
        await _session.RetryAsync(applicationId).ConfigureAwait(true);
        StatusMessage = "Nouvelle tentative terminée.";
    }

    private void SessionChanged(object? sender, EventArgs args) => RefreshOnUiThread();

    private void InstallationProgressChanged(object? sender, InstallationProgressEvent args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessage = args.Message;
            Refresh();
        });
    }

    private void RefreshOnUiThread()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        Recommendations.Clear();
        foreach (var item in _session.Recommendations)
        {
            Recommendations.Add(new RecommendationCardViewModel(
                item,
                selected => _session.SetSelected(item.ApplicationId, selected)));
        }

        ReportItems.Clear();
        if (_session.InstallationReport is not null)
        {
            foreach (var item in _session.InstallationReport.Items)
            {
                ReportItems.Add(new ReportItemViewModel(item, () => RetryAsync(item.ApplicationId)));
            }
        }

        RaiseAll();
        (NextCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (InstallCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelInstallationCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseAll()
    {
        foreach (var property in new[]
        {
            nameof(IsWelcome), nameof(IsMachineSummary), nameof(IsProfileSelection), nameof(IsRecommendations),
            nameof(IsReview), nameof(IsConfirmation), nameof(IsInstallation), nameof(IsReport), nameof(ShowNextButton),
            nameof(StepCounter), nameof(StepTitle), nameof(NextButtonText), nameof(ProfileLabel), nameof(SelectedCount),
            nameof(SelectionSummary), nameof(SucceededCount), nameof(FailedCount), nameof(SkippedCount), nameof(CancelledCount)
        })
        {
            OnPropertyChanged(property);
        }
    }

    private static string ProfileName(UserProfile profile) => profile switch
    {
        UserProfile.Personal => "Personnel",
        UserProfile.Development => "Développement",
        UserProfile.Business => "Business",
        UserProfile.Creation => "Création",
        UserProfile.Training => "Formation",
        _ => profile.ToString()
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= SessionChanged;
        _session.InstallationProgressChanged -= InstallationProgressChanged;
        _session.Dispose();
    }
}

public sealed class RecommendationCardViewModel : INotifyPropertyChanged
{
    private readonly Action<bool> _setSelected;
    private bool _isSelected;

    public RecommendationCardViewModel(GuidedRecommendationItem item, Action<bool> setSelected)
    {
        Item = item;
        _isSelected = item.IsSelected;
        _setSelected = setSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public GuidedRecommendationItem Item { get; }
    public string ApplicationName => Item.Decision.ApplicationName;
    public string Level => Item.Decision.Level switch
    {
        RecommendationLevel.Essential => "Essentiel",
        RecommendationLevel.Recommended => "Recommandé",
        _ => "Optionnel"
    };
    public string Status => Item.Decision.Disposition switch
    {
        RecommendationDisposition.Recommended => "Compatible",
        RecommendationDisposition.AlreadyInstalled => "Déjà installé",
        RecommendationDisposition.Incompatible => "Incompatible",
        RecommendationDisposition.CompatibilityUnknown => "Compatibilité à vérifier",
        RecommendationDisposition.InventoryUnknown => "État installé inconnu",
        RecommendationDisposition.Conflict => "Conflit",
        _ => "Indisponible"
    };
    public string Reason => Item.Decision.Reasons.FirstOrDefault()?.Message ?? Item.Decision.ProfileReasonKey;
    public bool CanSelect => Item.CanSelect;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value || (!CanSelect && value))
            {
                return;
            }

            _setSelected(value);
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed class ReportItemViewModel
{
    public ReportItemViewModel(InstallationItemSnapshot item, Func<Task> retry)
    {
        Item = item;
        RetryCommand = new AsyncRelayCommand(retry, () => item.CanRetry);
    }

    public InstallationItemSnapshot Item { get; }
    public string ApplicationId => Item.ApplicationId;
    public string Status => Item.State.ToString();
    public string Message => Item.Message ?? Item.DiagnosticCode ?? "Terminé.";
    public bool CanRetry => Item.CanRetry;
    public ICommand RetryCommand { get; }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _isExecuting;
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute().ConfigureAwait(true);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
