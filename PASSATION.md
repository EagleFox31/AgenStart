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

## 2026-11-05 15:48 — Passe 9 : sous-vues liées directement aux VM d'étape

Branche `refactor/desktop-bind-step-viewmodels` (depuis `main` à dda5ae1).

## Fait
- Les 6 vues d'étape ont un `x:DataType` sur leur VM d'étape (`steps:OverviewViewModel` pour
  Overview et YourPc, puis Recommendations, Review, Installation, Report).
- `MainWindow.axaml` : chaque vue est emballée dans un `<Panel IsVisible="{Binding IsX}">` et
  reçoit `DataContext="{Binding <SousVM>}"`. Le Panel évite le piège LL-005 : un `IsVisible` sur
  l'élément qui porte le nouveau DataContext serait résolu contre le sous-VM.
- Les règles transverses vont dans les sous-VM, via `ISessionState.IsInstallingChanged` :
  `RecommendationsViewModel.CanReview` et `ReviewViewModel.CanStartInstallation`.
  `ReviewViewModel` prend maintenant `ISessionState` dans son constructeur.
- `InstallationViewModel.HasReport` reflète `ReportViewModel.HasReport` (synchronisé par MWVM),
  pour le bouton « View report » sans dépendance Installation → Report.
- `MainWindowViewModel` : suppression de tous les passe-plats (propriétés et méthodes triviales),
  de `RelayChildPropertyChanged` et du `IsBusy` composite. Il reste la navigation, les sous-VM
  et l'orchestration transverse (`SelectProfile`, `BuildRecommendationsAsync`, `PrepareReview`,
  `RemoveFromReview`, `StartInstallationAsync`, rapport en fin d'installation).
- `MainWindow.axaml.cs` lit `_viewModel.<SousVM>.X` et abonne `ViewModel_OnPropertyChanged`
  aux sous-VM Review, Installation, Report et RecommendationsStep (les noms de propriétés
  gérés ne se recouvrent pas).

## Vérification
- `dotnet build src/AgenStart.Desktop/AgenStart.Desktop.csproj` : 0 erreur, 0 avertissement.
  Les bindings compilés sont actifs par défaut (`AvaloniaUseCompiledBindingsByDefault`) sans
  aucune exception, donc chaque binding des vues est vérifié contre son VM d'étape.
- **Pas fait** : passage visuel de chaque étape avec capture (prévention LL-005). Le pilotage
  automatique par UI Automation a été refusé dans cette session. À faire à la main : Overview →
  Analyse → Your PC → Usage profile → Build → Recommendations → Confirm (vérifier qu'une
  seule étape est visible à la fois et que les boutons Confirm et Start installation
  s'activent correctement).

## Reste à faire
1. Vérification visuelle ci-dessus, puis fusionner la PR.
2. Composition DI dans `App.axaml.cs` (inchangé).
