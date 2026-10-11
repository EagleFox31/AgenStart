# Frontend audit — AgenStart.Desktop (2026-10-11)

Audit du dossier `src/AgenStart.Desktop/` et plan de remise en ordre
incrémentale. Chaque étape est cochée au fur et à mesure de son achèvement.

## Constat

Le dossier `Views/` est devenu un empilement de rustines. `MainWindow` est
éclaté en **7 fichiers partiels** totalisant ~2050 lignes de code-behind pour
une seule fenêtre, avec des noms qui racontent l'histoire : `UiPolish`,
`StableUiPolish`, `RecommendationListExperience`, `RecommendationProgress`,
`Branding`, `MultiProfiles`. Ces fichiers sont des **couches sédimentaires**,
pas une architecture.

### Les 5 vrais problèmes

1. **Code-behind qui redessine l'UI après coup.** `UiPolish.cs` et
   `StableUiPolish.cs` parcourent l'arbre visuel (20 occurrences) *après layout*
   pour muter padding, spacing, polices, column widths et ajouter des contrôles
   entiers (guidance box, loading card, badges sémantiques). Le commentaire
   `MainWindow.UiPolish.cs:40-43` avoue que ce patron a déjà causé une
   StackOverflowException en prod, contournée par un `Dispatcher.UIThread.Post`
   — la cause racine n'a jamais été retirée.
2. **Couleurs et dimensions dupliquées** entre `AgenStartTheme.axaml` et le
   code-behind (`Color.Parse("#176D64")`, etc. — 20 occurrences dans 4 fichiers
   `.cs`), alors que les brushes équivalents existent déjà dans le thème.
3. **XAML monolithique**. `MainWindow.axaml` empile 7 "pages" dans la même
   grille avec `IsVisible="False"` et un code-behind qui bascule manuellement
   les `IsEnabled` de la sidebar à chaque transition.
4. **Templates inline sur une seule ligne de 2000 colonnes** pour les listes
   Review / Installation / Report. Illisibles, non-diffables, structure
   partagée dupliquée 3 fois.
5. **ViewModel plat de 930 lignes** avec 40+ propriétés nommées
   (`ReviewInstallCount`, `InstallationCompletedCount`, …). Devrait être
   découpé en sous-VMs par étape.

## Plan de remise en ordre

Chaque étape est petite, livrable, testable au build.

- [x] **1. Consolider les couleurs** ✅ _2026-10-11, build vert_
      Remplacer tous les `Color.Parse(...)` et `SolidColorBrush` hardcodés des
      partiels par des lookups `this.FindResource(...)` sur les brushes du
      thème. Ajouter au thème les brushes manquants (sémantiques warning /
      danger / soft variants, hover profile). Zéro changement visuel attendu.
      → Thème enrichi (badge success/warning/danger + borders, profile hover,
      recommendation sticky). Helper `ThemeBrush(key)` ajouté sur `MainWindow`.
      22 constantes `Color.Parse` supprimées des partiels.
- [x] **2. Extraire les styles purement cosmétiques dans le thème** ✅ _2026-10-11, build vert_
      → Classes XAML ajoutées au thème : `Button.nav TextBlock.navIcon` /
      `.navLabel` (sidebar), `Border.dataRow TextBlock.rowIcon` (icône YourPc),
      `Border.statusBadge.success|warning|danger` (badges YourPc).
      → `MainWindow.axaml` : sidebar nav passée aux classes `navIcon`/`navLabel`
      (StackPanel Spacing=0), dataRows passés à `ColumnDefinitions="46,*,150"`
      + `ColumnSpacing="16"`, status TextBlocks remplacés par des badges
      déclaratifs `<Border Classes="statusBadge success">`.
      → `MainWindow.StableUiPolish.cs` supprimé (-203 lignes). `PolishMachineRows`
      (dead code) retiré. Appel `Dispatcher.Post(ApplyStableUiPolish)` retiré.
      Plus aucun walk de l'arbre dans la sidebar ou le panneau YourPc.
- [x] **3. Extraire les listes réutilisables** ✅ _2026-10-11, build vert_
      → `AppRowLogoView.axaml` + `.axaml.cs` créés : UserControl rendant le
      logo pill (initiales + image conditionnelle) partagé Review / Installation
      / Report. Taille par défaut 38×38 ; Installation / Report override à
      36×36 au point d'appel.
      → Interface `IAppRowLogo` ajoutée (Initials / IconSource / HasLogo /
      ShowInitials) implémentée par `ReviewRowViewModel`,
      `InstallationRowViewModel`, `ReportRowViewModel`.
      → Les 3 templates `DataTemplate` inline (Review / Installation / Report)
      consomment maintenant `<views:AppRowLogoView />` (~6 lignes
      déduplicées × 3).
      → **Recommendation garde son logo inline** : structure visuelle
      différente (Border 48×48 teal-tinté + Image 30×30), pas candidat au
      template partagé sans paramétrage lourd.
- [ ] **4. Découper MainWindow.axaml en UserControls**
      Un `UserControl` par étape : `OverviewView`, `YourPcView`,
      `UsageProfilesView` (déjà existant côté profils multiples),
      `RecommendationsView`, `ReviewView`, `InstallationView`, `ReportView`.
      Navigation déclarative via `ContentControl` + `CurrentStep` dans le VM.
      Supprimer les 7 handlers `*Button_OnClick` + les `*Stripe` manuels.
- [ ] **5. Découper le ViewModel**
      Introduire `OverviewViewModel`, `RecommendationsViewModel`,
      `ReviewViewModel`, `InstallationViewModel`, `ReportViewModel`. Les 40
      propriétés plates deviennent 5 propriétés d'objets. `MainWindowViewModel`
      ne garde que l'orchestration (step courante, snapshot, profil).
- [ ] **6. Normaliser la sidebar**
      Remplacer les 7 blocs `<Grid><Border Stripe /><Button nav /></Grid>`
      par un `ItemsControl` piloté par une liste de `NavItemViewModel`.
      Supprime la logique `IsEnabled` manuelle.
- [ ] **7. Retirer les partiels devenus vides**
      Au fil des étapes 2–6, supprimer `MainWindow.UiPolish.cs`,
      `MainWindow.StableUiPolish.cs`, `MainWindow.RecommendationListExperience.cs`,
      `MainWindow.RecommendationProgress.cs`, `MainWindow.MultiProfiles.cs`,
      `MainWindow.Branding.cs`.

## Hors scope de cet audit

- Pas de changement de design visuel. L'objectif est la santé structurelle, pas
  un redesign.
- Pas de refonte des services métier (`Application`, `Catalogue`,
  `Recommendations`, …).
- Pas de migration vers ReactiveUI ou CommunityToolkit.Mvvm sauf si ça retire
  plus de code qu'il n'en ajoute.
