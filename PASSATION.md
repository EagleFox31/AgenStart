# Passation — Refonte frontend AgenStart.Desktop

## Objectif en cours
Supprimer les partiels sédimentaires `MainWindow.*.cs` du projet Avalonia en
migrant leur contenu vers du XAML déclaratif. Plan complet :
[docs/engineering/frontend-audit-2026-10.md](docs/engineering/frontend-audit-2026-10.md).

## Commits sur `main` (local, non pushé)
- `c89a5aa` refactor(desktop): migrate MainWindow partials to declarative XAML
  — thème, états `AppStep`, nav full-width, 3 partiels morts supprimés (-650 l).
- `ceac935` fix(windows): report Windows 11 for builds >= 22000.
- `900fe0f` chore: docs, golden tests, CI build/test split, .gitignore.
- `b20c762` refactor(desktop): inline UsageProfilesView in MainWindow XAML
  — `<views:UsageProfilesView>` directement dans le XAML, 5 handlers de
  profils + `BuildRecommendationsButton_OnClick` retirés.
- `6844031` refactor(desktop): drop MainWindow visual-tree walks for order
  and Confirm label — `OrderLabel` sur `RecommendationRowViewModel` maintenu
  via `CollectionChanged`, terminologie "Confirm" en XAML, retrait de
  `DecorateRecommendationRowsWithOrder` + `ApplyConfirmTerminology`.
- `d76ccb6` refactor(desktop): extract Report/Installation/Review panels
  into UserControls — 3 UC avec events (Finish/Export, Cancel/ViewReport/Retry,
  Start/Back/Remove), suppression du walk `FindButtonByContent("Export this setup")`.

## Fait côté étape 4b (3 passes surgicales)
- Partiel `MainWindow.MultiProfiles.cs` : plus de `Children.Clear/Add`,
  juste du wiring d'événements sur `UsageProfilesControl`.
- Partiel `MainWindow.RecommendationListExperience.cs` : réduit à ~180 l,
  ne contient plus que la sticky-header pilotée par le scroll offset.

## WIP parallèle (non commité, à moi de ne pas écraser)
Chantier ADR 0003 "data-driven usage profile registry" en cours côté
utilisateur :
- Nouveaux : `src/AgenStart.Core/Profiles/ProfileDefinition.cs` +
  `ProfileRegistry.cs`, `docs/product/profiles.json` + schema,
  `src/AgenStart.Desktop/ViewModels/ProfileOptionViewModel.cs`,
  `tests/AgenStart.Catalogue.Tests/ProfileRegistryTests.cs`.
- Modifs : `AgenStart.Core.csproj`, `SoftwareCatalogueLoader.cs`,
  `RecommendationEngine.cs`, `software-catalogue.schema.json`,
  `UsageProfilesView.axaml[.cs]`, `Program.cs`, workflows CI,
  `lessons-learned.md`, `tests/AgenStart.Catalogue.Tests/SoftwareCatalogueLoaderTests.cs`.
- **Règle** : ne pas inclure ces fichiers dans un commit "refonte frontend".
  Toujours `git status --short` avant chaque `git add` et stager uniquement
  les fichiers explicitement touchés par la passe en cours.

## Reste à faire (ordre)
1. **Validation visuelle** sticky-header + ordinal + "Confirm" + Report/
   Installation/Review après rebuild (3 UC fraîchement extraits).
2. **Étape 4 (suite)** — Extraire les 3 panels restants :
   - `OverviewView` : attention au walk `FindButtonByContent("Import a setup")`
     et à l'injection du `_importStatusText` dans le StackPanel parent du
     bouton Import. Remplacer par des événements `AnalyzeRequested`,
     `ImportSetupRequested` + binding/méthode pour le status d'import.
   - `YourPcView` : simple (ContinueToProfile + RefreshButton).
   - `RecommendationsView` : fusion avec la sticky-header de
     `MainWindow.RecommendationListExperience.cs` (étape 4b suite).
3. **Étape 4b suite** — Déplacer la sticky-header dans `RecommendationsView`,
   puis supprimer `MainWindow.RecommendationListExperience.cs`.
4. **Étape 5** — Découper `MainWindowViewModel` (~988 l) en sous-VMs par étape.
5. **Étape 6** — Sidebar en `ItemsControl` + `NavItemViewModel`.
6. **Étape 7 (fin)** — Supprimer `MainWindow.MultiProfiles.cs` +
   `MainWindow.RecommendationListExperience.cs`.

## Conventions / pièges
- Build : `dotnet build src/AgenStart.Desktop/AgenStart.Desktop.csproj -v q`.
  Fermer l'app avant rebuild (lock DLL). Pas de .sln à la racine.
- Commits et PR **sans attribution** (règle globale ; pas de
  `Co-Authored-By`, pas de mention Claude/IA/assistant).
- `dotnet test` → exit 5 "zéro tests" sur Desktop (préexistant, ignorer).
- Git identity locale au repo : `EagleFox31 / lawrynnjennifer@gmail.com`.
