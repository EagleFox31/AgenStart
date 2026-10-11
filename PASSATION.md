# Passation — Refonte frontend AgenStart.Desktop

## Objectif en cours
Supprimer les partiels sédimentaires `MainWindow.*.cs` du projet Avalonia en
migrant leur contenu vers du XAML déclaratif. Plan complet :
[docs/engineering/frontend-audit-2026-10.md](docs/engineering/frontend-audit-2026-10.md).

## Fait (build .NET vert, non commité)
- **Étape 1 — Couleurs** : brushes thème ajoutés
  ([AgenStartTheme.axaml:18-65](src/AgenStart.Desktop/Styles/AgenStartTheme.axaml#L18-L65)) + helper
  `ThemeBrush(key)` ([MainWindow.axaml.cs:64](src/AgenStart.Desktop/Views/MainWindow.axaml.cs#L64)).
- **Étape 2 — Styles cosmétiques** : classes `Button.nav`, `Border.dataRow`,
  `Border.statusBadge.success|warning|danger` ([AgenStartTheme.axaml:82-320](src/AgenStart.Desktop/Styles/AgenStartTheme.axaml)).
- **Étape 3 — AppRowLogoView** : [IAppRowLogo.cs](src/AgenStart.Desktop/ViewModels/IAppRowLogo.cs)
  + UserControl [AppRowLogoView.axaml](src/AgenStart.Desktop/Views/AppRowLogoView.axaml) consommés par Review/Installation/Report.
- **Étape 4a — Navigation état-dirigée** : enum `AppStep` + `CurrentStep` +
  9 bool helpers sur [MainWindowViewModel.cs:20-110](src/AgenStart.Desktop/ViewModels/MainWindowViewModel.cs#L20-L110).
  7 nav buttons + 7 panels + 7 stripes liés via `Classes.active` / `IsVisible`,
  dispatch unique `NavButton_OnClick` via `Tag`.
- **Fix OS** : [WindowsMachineInventoryProvider.cs:97-103](src/AgenStart.Platform.Windows/Inventory/WindowsMachineInventoryProvider.cs#L97-L103)
  substitue "Windows 11" sur `build >= 22000` (quirk registre Microsoft).
- **Fix hover `dataRow`** : `Background="Transparent"` + style `:pointerover`
  ([AgenStartTheme.axaml:230-250](src/AgenStart.Desktop/Styles/AgenStartTheme.axaml#L230-L250)) → ligne entière hit-testable.
- **Fix nav désactivé** : règle `Button.nav:disabled /template/ ContentPresenter`
  manquante → Fluent appliquait son `ButtonBackgroundDisabled` autour du texte
  ([AgenStartTheme.axaml:121-125](src/AgenStart.Desktop/Styles/AgenStartTheme.axaml#L121-L125)).
- **Nav full-width style VS Code** ([AgenStartTheme.axaml:82-93](src/AgenStart.Desktop/Styles/AgenStartTheme.axaml#L82-L93)) :
  `Margin=0`, `CornerRadius=0`, `Padding=30,0`, `HorizontalAlignment=Stretch` ;
  `AgenNavHoverColor` passé de `#12313C` → `#1A3F4B` (contraste visible).
- **Étape 7 (partielle) — 3 partiels morts supprimés** : `Branding.cs` (-93),
  `UiPolish.cs` (-283), `RecommendationProgress.cs` (-283). Jamais appelés depuis
  commit `dca043c`. Brushes `Guidance*`/`TealBrush`/`ProfileDefaultBorderBrush`
  consommés par `RecommendationListExperience` inlinés en getters locaux
  ([MainWindow.RecommendationListExperience.cs:14-18](src/AgenStart.Desktop/Views/MainWindow.RecommendationListExperience.cs#L14-L18)). **-650 lignes net.**

## Décisions prises
- **Pas de redesign visuel** : seules divergences thème↔code préservées.
- **Pas d'`AppRowView` complet** : status/actions trop divergents entre vues.
- **Panels inline dans MainWindow.axaml** pour l'instant (étape 4b en attente).
- **Nav sans marge horizontale** : alignement VS Code demandé par l'utilisateur.
- **Pas de commit automatique** — attendre validation visuelle.

## Reste à faire (ordre)
1. **Validation visuelle** : nav full-width + hover visible + rows `dataRow` hoverables.
2. Étape 4b — Extraire chaque panel inline en `UserControl` + `ContentControl`
   lié à `CurrentStep`. Migrer en même temps les visual-tree walks des 2 partiels restants.
3. Étape 5 — Découper `MainWindowViewModel` (~976 lignes) en sous-VMs par étape.
4. Étape 6 — Sidebar en `ItemsControl` + `NavItemViewModel`.
5. Étape 7 (fin) — Supprimer `MultiProfiles.cs` + `RecommendationListExperience.cs`.

## Pièges / contexte
- Build : `dotnet build src/AgenStart.Desktop/AgenStart.Desktop.csproj -v q` (~2s).
  Fermer l'app avant rebuild (lock DLL).
- Run : `dotnet run --project src/AgenStart.Desktop` (pas de tests UI).
- `dotnet test` → exit 5 "zéro tests" (préexistant, ignorer).
- Partiels restants : `MultiProfiles.cs` (remplace `UsageProfilePanel` par
  `UsageProfilesView` + subscribe `RecommendationPipelineDiagnostics`),
  `RecommendationListExperience.cs` (sticky header + numérotation rows +
  renommage "Review"→"Confirm"). Les deux walkent le visual tree, à migrer.
- Convention : commits & PR sans mention assistant (règle globale).
- Pas de .sln à la racine.
