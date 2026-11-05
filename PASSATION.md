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

## 2026-11-05 16:06 — PR #117 : correctif CI + passe de fumée + refonte thème décidée

- **CI #117** : l'étape `Verify formatting` échouait (espace manquante, `RecommendationsViewModel.cs`). Corrigé dans 98c1f09, avec la leçon LL-006 (lancer `dotnet format ... --verify-no-changes` avant chaque push). La CI repasse.
- **Passe de fumée** : script UIA hors dépôt (scratchpad de session, `smoke.ps1`), lancé par l'utilisateur dans son terminal, parce que le mode auto bloque son exécution par l'agent. Il ne clique jamais sur « Start installation ». Premier essai : Overview seul visible, puis plantage `E_UNEXPECTED` (élément UIA périmé), corrigé par une nouvelle recherche des éléments. Second essai en cours.
- **Décision (utilisateur)** : après la fusion de #117, refonte du thème d'AgenStart dans le style de `claude-account-manager` (jetons Apple). Clair uniquement, sans sombre. Trois thèmes au choix : Apple, Google (Material 3) et AgenStart (actuel).
  - Plan : jetons dans des `ThemeDictionaries` ; 184 `StaticResource` (12 vues) passent en `DynamicResource` ; sélecteur dans Settings (aujourd'hui désactivé) ; préférence enregistrée localement.
- **Prochaine étape** : résultat de la passe de fumée → CI verte → fusion de #117 par l'utilisateur → branche depuis `main` pour le thème.

## 2026-11-05 17:08 — Refonte graphique : thèmes clairs au choix (branche empilée)
- **Branche** : `feat/desktop-selectable-themes` (a56fda0, poussée), empilée sur `refactor/desktop-bind-step-viewmodels` (PR #117, CI verte, pas encore fusionnée). Aucune PR ouverte pour les thèmes.
- **Fait** :
  - 3 palettes claires dans `src/AgenStart.Desktop/Styles/Palettes/` (AgenStart, Apple, Google), avec les mêmes clés partout : couleurs, `AgenFontFamily`, rayons, marges de navigation.
  - `Services/ThemeService.cs` (`AppThemes`, `AvaloniaThemeService`) change le dictionnaire fusionné à chaud et règle l'accent Fluent.
  - Les vues passent toutes en `DynamicResource` ; plus aucune couleur codée en dur.
  - Choix du thème dans Settings (ComboBox), enregistré dans `DesktopSettings.Theme`, qui reste compatible avec les anciens fichiers. `App.Initialize` applique le thème enregistré avant l'ouverture de la fenêtre.
  - Barre de progression indéterminée + « Scanning… » pendant l'inventaire.
- **Vérifié** : build 0 erreur 0 avertissement, `dotnet format --verify-no-changes` OK.
- **Non vérifié** : le rendu visuel. Les captures automatiques ont été refusées par le classifieur. L'utilisateur teste les 3 thèmes à la main.
- **Prochaine étape** : retours visuels → retouches → PR (base #117, ou `main` après fusion) avec la section « RAIDER review », sans mention d'IA.

## 2026-11-05 17:35 — Refonte graphique complète desktop (inset grouped)

- Branche `feat/desktop-selectable-themes`, commit `269fa38` : sidebar claire, cartes groupées, lignes séparées, tuiles de stats et hero sur toutes les étapes (Overview → Report, History, Settings). Les palettes Apple / Google / AgenStart pilotent aussi rayons, ombres, bordures et métriques de navigation.
- Contrats conservés : `x:Name`, handlers, bindings, textes de boutons du smoke test, libellés « History » / « Settings » (FindNavigationButton).
- Preuves : build 0 erreur / 0 avertissement, `dotnet format --verify-no-changes` OK, tests 132/132 (`dotnet test --project <csproj> -c Release` ; sans `--project` sous MTP → 0 test, code 5).
- Reste : contrôle visuel des 3 thèmes dans l'app (fermer l'instance en cours avant `dotnet run`), PR à revoir, re-vérifier la CI de #117.

## 2026-11-05 17:37 — En attente du test visuel utilisateur

- État : PR #118 (brouillon, `feat/desktop-selectable-themes`, HEAD `a28df7f`) ; #117 CI verte, fusion = décision utilisateur.
- Verrou : instance AgenStart ouverte bloque `bin\Debug` → la fermer avant `dotnet run`, ou builder avec `-p:OutDir=<scratchpad>`.
- Prochaine étape : appliquer les retours visuels (Apple / Google / AgenStart), puis sortir #118 du brouillon.

## 2026-11-05 18:10 — Correctif crash au démarrage (logo PNG)

- `dotnet run` sortait sans fenêtre : le PNG `agenstart-app-icon.png` (palette 8 bits) n'est pas décodable par Skia. Remplacé par une pastille vectorielle thémée (MainWindow + Settings). LL-008 consigné.
- Preuve : exe lancé 15 s sans exception (exit 124), build 0/0, format OK.
- Prochaine étape : test visuel utilisateur des 3 thèmes, puis sortir #118 du brouillon.
