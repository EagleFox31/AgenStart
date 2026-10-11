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
- `26c2720` refactor(desktop): extract YourPcView UserControl —
  ContinueToProfile + Refresh events, panneau 100% déclaratif.
- `ded6d89` refactor(desktop): extract OverviewView UserControl —
  Analyse/Import events, status d'import en `TextBlock` nommé dans l'UC ;
  retire la recherche `FindButtonByContent("Import a setup")` et
  l'injection dynamique de `_importStatusText`.
- `5d2b68b` refactor(desktop): extract RecommendationsView and inline
  sticky-header — events Review/SelectEssentials ; sticky-header déclaratif
  bindé sur `RecommendationCount`/`SelectedCount`, visibilité pilotée par
  `MainScrollViewer.Offset.Y`. Supprime `MainWindow.RecommendationListExperience.cs`
  (walks de l'arbre visuel, construction dynamique de la sticky, FindButtonByContent)
  et le helper `ThemeBrush`.

## Fait côté étape 4b
- Partiel `MainWindow.MultiProfiles.cs` : plus de `Children.Clear/Add`,
  juste du wiring d'événements sur `UsageProfilesControl` ; appelle
  `UpdateRecommendationStickyHeader()` après build.
- Partiel `MainWindow.RecommendationListExperience.cs` : supprimé.

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
1. **Validation visuelle** après rebuild : Overview (Analyse / Import +
   status text), YourPc (Choose profile / Refresh), Recommendations (sticky
   header, Confirm, Select essentials), ordinal + Report/Installation/Review.
2. **Étape 5** — Découper `MainWindowViewModel` (~988 l) en sous-VMs par étape.
3. **Étape 6** — Sidebar en `ItemsControl` + `NavItemViewModel`.
4. **Étape 7 (fin)** — Supprimer `MainWindow.MultiProfiles.cs` (dernier
   partiel restant).

## Conventions / pièges
- Build : `dotnet build src/AgenStart.Desktop/AgenStart.Desktop.csproj -v q`.
  Fermer l'app avant rebuild (lock DLL). Pas de .sln à la racine.
- Commits et PR **sans attribution** (règle globale ; pas de
  `Co-Authored-By`, pas de mention Claude/IA/assistant).
- `dotnet test` → exit 5 "zéro tests" sur Desktop (préexistant, ignorer).
- Git identity locale au repo : `EagleFox31 / lawrynnjennifer@gmail.com`.
