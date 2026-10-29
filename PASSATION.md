# Passation — Refonte frontend AgenStart.Desktop

## Objectif en cours
Passe 9 (optionnelle) : rebinder les DataContext XAML directement sur les
sous-VM pour retirer les passe-plats de `MainWindowViewModel`.

## Fait (committé 57d93eb)
- Passe 8 terminée. MWVM 737 → 400 lignes, devient coordinateur mince.
- `AnalyzeAsync` + `IsBusy` dans
  [OverviewViewModel.cs](src/AgenStart.Desktop/ViewModels/Steps/OverviewViewModel.cs)
  (ctor `(IMachineInventoryProvider, ISessionState)`, écrit
  `_session.SetSnapshot`).
- `BuildAsync` + `SelectEssentialsOnly` + `ClearRows` + event
  `SelectionChanged` + `IsBusy` dans
  [RecommendationsViewModel.cs](src/AgenStart.Desktop/ViewModels/Steps/RecommendationsViewModel.cs)
  (ctor avec `IInstalledSoftwareInventoryProvider`, `SoftwareStateResolver`,
  `RecommendationEngine`, `SoftwareCatalogueLoader`, `ISessionState`).
- Orchestrateur/session + dispatcher UI + `StartAsync`/`Cancel`/`RetryAsync`
  + events `Starting`/`Completed` + `IDisposable` dans
  [InstallationViewModel.cs](src/AgenStart.Desktop/ViewModels/Steps/InstallationViewModel.cs)
  (ctor `(ISessionState, IUiDispatcher, IInstalledSoftwareInventoryProvider,
  SoftwareStateResolver)` ; setter `IsInstalling` relaie à `ISessionState`).
- [MainWindowViewModel.cs](src/AgenStart.Desktop/ViewModels/MainWindowViewModel.cs)
  réécrit en façade (délègue AnalyzeAsync, BuildRecommendationsAsync,
  SelectEssentialsOnly, StartInstallationAsync, CancelInstallation,
  RetryInstallationAsync). Garde `PrepareReview`, `RemoveFromReview`,
  `SelectProfile`, `ClearRecommendations` (cross-cut). `IsBusy` composite.
- `MainWindow.MultiProfiles.cs` supprimé, contenu fusionné dans
  [MainWindow.axaml.cs](src/AgenStart.Desktop/Views/MainWindow.axaml.cs)
  (`InstallMultiProfileExperience` appelé depuis `OnOpened`).

## Décisions prises
- **MWVM reste façade** : vues continuent de lire `_viewModel.X` via
  passe-plats, pas de rebind XAML dans cette passe → minimise la surface
  de régression.
- **`ISessionState` comme bus partagé** : porte `Snapshot`, `Catalogue`,
  `LastReport`, `IsInstalling`. Les sous-VM lisent via session, pas via MWVM.
- **Events sortants d'InstallationVM** (`Starting`/`Completed`) : évitent
  à InstallationVM de connaître `ReportViewModel`. MWVM relaie à Report.
- **`RecommendationsVM.SelectionChanged`** : MWVM écoute pour appeler
  `ResetReviewOnly()` sans fuite de dépendance Recommendations → Review.
- **`IsBusy = Overview.IsBusy || Recommendations.IsBusy`** : composite
  calculé, pas de champ sur MWVM. Relayé par `OnPropertyChanged(nameof(IsBusy))`
  sur signal des sous-VM.

## Reste à faire (ordre)
1. **Passe 9 (optionnelle)** — rebinder DataContext des sous-vues
   directement sur les sous-VM, puis supprimer les passe-plats de MWVM
   (StatusTitle, OperatingSystem, HasReview, ReportProcessedCount, …).
   Risque : chaque XAML à relire.
2. **Pattern DI** — remonter la composition dans `App.axaml.cs` ou un
   container minimal plutôt que `new ...()` dans `MainWindow` ctor.
3. ~~Commit de la passe 8~~ fait (57d93eb).

## Pièges / contexte
- Build : `dotnet build src/AgenStart.Desktop/AgenStart.Desktop.csproj -v q`.
  Fermer l'app avant rebuild (lock DLL). Pas de .sln à la racine.
- `dotnet test` → exit 5 "zéro test" (préexistant, ignorer).
- WIP non committée à préserver : untracked `site/assets/generated-brand/`,
  `site/assets/generated-logos/`. Toujours `git status --short` avant
  `git add` ; stager fichier par fichier.
- `InstallationViewModel.IsInstalling` écrit maintenant à `ISessionState`
  via son setter — ne pas court-circuiter avec `_session.SetIsInstalling`
  séparément.
- `BuildInitials` dupliqué dans InstallationVM (statique privé). Rangement
  possible plus tard dans un helper si réutilisé.
- Commits / PR **sans attribution** (règle globale).
- Git identity locale : `EagleFox31 / lawrynnjennifer@gmail.com`.
