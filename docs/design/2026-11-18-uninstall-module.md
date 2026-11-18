# Conception — module de désinstallation propre

- **Date** : 2026-11-18
- **Statut** : proposition (P0, conception seule, aucun code produit)
- **Issue** : #119
- **Portée** : application desktop Windows (Avalonia, .NET 10)
- **Dépendance** : la navigation par onglets et les leçons LL-006 à LL-009 arrivent avec la PR #118,
  qui n'est pas encore fusionnée. P1 démarre après sa fusion.

## 1. Objectif et positionnement

Désinstaller un logiciel par sa **voie officielle** (silencieuse si possible), puis proposer ses
**restes** avec une justification et une taille pour chacun. Les **données utilisateur sont protégées
par défaut**, et toute suppression est réversible (corbeille ou export `.reg`).

AgenStart ne supprime que ce qu'il a listé et que l'utilisateur a coché.

Ce qui distingue AgenStart d'IObit Uninstaller :

- pas de bundleware, de publicité, de service en arrière-plan, ni de « score de santé » ;
- le plan complet est visible avant toute action ;
- les règles sont ouvertes et versionnées ;
- la corbeille est le comportement par défaut.

### Non-objectifs (v1)

- Nettoyeur de registre, pilotes, fonctionnalités Windows, mises à jour.
- Extensions de navigateur, gestion du démarrage.
- Surveillance d'installation en temps réel (type Revo).
- Désinstallation « forcée » sans désinstalleur officiel.
- Désinstallation par lot (un logiciel par session en v1).
- Services Windows : ils sont laissés au désinstalleur officiel et seulement signalés.

## 2. Reuse-first

| Candidat | Constat | Décision |
|---|---|---|
| Bulk Crap Uninstaller (`Klocman/Bulk-Crap-Uninstaller`) | Apache-2.0, actif. `UninstallTools` cible `net8.0-windows` + WinForms et dépend de `KlocTools`, `ObjectListView`, `System.Management`, `TaskScheduler` ; pas de paquet NuGet. Sa détection de restes est du code impératif. | **Learn** : reprendre la méthode (scores de confiance signés de `Junk/Confidence/ConfidenceRecords.cs`, liste des détecteurs, détection du type d'installeur). Aucun code copié ; un éventuel portage exige un NOTICE Apache-2.0 et un ADR. |
| `winget uninstall` (CLI) | Déjà utilisé par `WinGetProvider`, `WinGetCommandBuilder`, `WinGetProcessRunner`, `WinGetResultNormalizer`. | **Adapt** : ajouter `BuildUninstall`, pas de chemin parallèle. |
| `Microsoft.WinGet.Client` / COM `Microsoft.Management.Deployment` | Exige un TFM Windows, ComInterop, l'activation COM et a des limites en contexte élevé ; ce serait un second chemin à côté de la CLI. | **Learn** (correspondance des options) ; rejeté pour la v1. |
| Windows Installer (`msiexec /x`, `MsiQueryProductStateW`) | API de l'OS. | **Adopt** : P/Invoke minimal + CLI msiexec, sans WixToolset.Dtf (trop lourd pour deux fonctions). |
| Appx `PackageManager` | Exige `net10.0-windows10.0.19041`, alors que tous les projets sont en `net10.0`. | **Reporté en P6** : via winget ou par un ADR sur le TFM. |
| Corbeille | `IFileOperation` + `FOFX_RECYCLEONDELETE` / `FOF_WANTNUKEWARNING`. `Microsoft.VisualBasic.FileSystem` peut supprimer définitivement en silence au-delà du quota. | **Adopt** l'OS, avec vérification préalable du quota. |
| Processus verrouillant des fichiers | Windows Restart Manager (`RmGetList`). | **Adopt**. |
| Faux système de fichiers pour les tests | TestableIO.System.IO.Abstractions : surface large ; ne modélise ni les points d'analyse ni la corbeille. | **Build** de ports étroits avec des fakes en mémoire. |

## 3. Parcours utilisateur

1. **Accès.** Nouvel onglet **« Désinstaller »** dans le groupe utilitaire (à côté d'Historique et
   Paramètres). Il est toujours accessible, sans condition (LL-007).
2. **Liste (panneau gauche).** Les logiciels installés, avec recherche et tri par nom, éditeur,
   taille ou date. Chaque ligne porte deux badges :
   - **source** : Registre, winget ou MSIX ;
   - **portée** : utilisateur ou machine.
3. **Plan (panneau droit).** La sélection d'un logiciel calcule son plan, **sans aucune écriture**.
   Le plan a trois sections :
   - **Désinstallation officielle** :
     - stratégie retenue et aperçu de la commande ;
     - « silencieuse » ou « assistant de l'éditeur » ;
     - badge UAC si une élévation est nécessaire.
   - **Restes détectés** : cases à cocher avec chemin, taille, confiance et justification
     dépliable.
     - Confiance élevée : pré-coché.
     - Confiance moyenne : décoché.
   - **Données utilisateur protégées** (sauvegardes, configuration, tout ce qui est sous
     OneDrive) : toujours décochées, avec un avertissement si l'utilisateur en coche une.
4. **Processus actifs.** Ils sont détectés via Restart Manager et via les images situées sous
   `InstallLocation`. La fermeture se fait processus par processus, chacun avec son propre
   accord explicite :
   1. tentative de fermeture propre (`CloseMainWindow`) ;
   2. si le processus résiste après un délai : `Kill`, uniquement après un second accord.
5. **Confirmation.** Un récapitulatif affiche :
   - le nombre d'éléments et le volume en octets ;
   - la mention « envoyé à la corbeille » ;
   - l'élévation éventuelle.

   Suivent l'exécution avec progression, la vérification, puis le rapport.
6. **Export.** « Exporter le plan » (JSON) permet une revue manuelle, dès P2.

## 4. Architecture

### Cœur : `src/AgenStart.Uninstall` (`net10.0`, pur)

Ce projet référence `SoftwareInventory` et `PackageManagement`.

- **Modèle**
  - `InstalledProgram`
    - Identité : `Id`, stable, est un hachage de la source et du chemin de clé.
    - Descriptif : `DisplayName`, `Publisher`, `Version`, `Scope`, `Source`.
    - Emplacements : `RegistryKeyPath`, `InstallLocation`.
    - Désinstallation : `UninstallString`, `QuietUninstallString`, `MsiProductCode`,
      `InstallerKind`.
    - Autres : `EstimatedSizeBytes`, `WinGetPackage?`.
  - `UninstallPlan` : `PlanId`, `ProgramId`, `StateFingerprint`, `OfficialStep`, `Leftovers`,
    `ProtectedUserData`, `Processes`, `Diagnostics`.
  - `PlanItem`
    - Identité : `ItemId`, `RuleId`.
    - Nature : `Kind` ∈ {Directory, File, Shortcut, RegistryKey, RegistryValue, ScheduledTask,
      FirewallRule}.
    - Cible : `Target`, `SizeBytes`, `Fingerprint`.
    - Évaluation :
      - `Confidence` (score, niveau, raisons) ;
      - `DataClass` ∈ {AppData, Cache, Configuration, UserData, Shortcut} ;
      - `DefaultSelected`, `RequiresElevation`.
  - `UninstallApproval` : `PlanId`, `SelectedItemIds`, `ApprovedProcessIds`,
    `AllowPermanentDelete = false`.
- **Ports**
  - Lecture : `IInstalledProgramInventory`, `IFileSystemProbe`, `IRegistryProbe`,
    `IKnownFolderResolver`, `IProcessInspector`. Ils n'ont **aucune méthode d'écriture**, ce qui
    garantit que P1-P2 sont en lecture seule par construction.
  - Action : `IOfficialUninstaller`, `ILeftoverRemover` (corbeille + sauvegarde),
    `IUninstallJournal`, `IElevatedExecutor`.
- **Logique**
  - Règles : `UninstallRuleSet` et son chargeur (même schéma que `SoftwareCatalogueLoader`),
    `LeftoverRuleEngine`.
  - Évaluation : `PathSafetyPolicy`, `ConfidenceScorer`.
  - Planification : `UninstallPlanner`, `OfficialStrategySelector`.

### Contrats de `PackageManagement`

- Nouvelle capacité **optionnelle** :
  `IPackageUninstallProvider : IPackageProvider`, avec
  `UninstallAsync(PackageUninstallRequest, CancellationToken)`.
- `PackageUninstallRequest(ApplicationId, ProviderPackageReference Package, bool Silent = true)`.
- Même modèle que `IPreparablePackageProvider` : `IPackageProvider` reste **inchangé**, donc les
  fakes existants restent verts.
- Réutilisation de `PackageOperationStatus` :
  - `NotFound` = no-op idempotent ;
  - `RebootRequired`, `CancelledByUser` et `RequiresElevation` sont repris tels quels.

### Application : `src/AgenStart.Application/Uninstallation/`

Ce dossier est calqué sur `Installation/`.

- `UninstallSession`, avec les états Ready → Running → Cancelling → Completed / Cancelled.
- `UninstallProgressEvent` et `UninstallReport`.
- `IUninstallVerifier`, sur le modèle de `IInstallationVerifier`.
- `UninstallOrchestrator` refuse tout élément absent de `UninstallApproval` (équivalent de
  `selection.not-approved`).

### Plateforme : `src/AgenStart.Platform.Windows/Uninstall/`

- **`UninstallRegistryReader`**
  - Il est extrait des sondes de `RegistryInstalledSoftwareCollector`.
  - Il lit toutes les valeurs utiles : `InstallLocation`, `UninstallString`,
    `QuietUninstallString`, `WindowsInstaller`, `Inno Setup: App Path`, `EstimatedSize`,
    `InstallDate`.
  - Le collecteur existant mappe depuis ce lecteur avec une **sortie identique**.
  - `InstalledSoftwareRecord` n'est **pas modifié**, car son égalité de record sert à la
    déduplication `HashSet`.
- **Adaptateurs**
  - `WindowsKnownFolderResolver` : `SHGetKnownFolderPath`, jamais de chemin en dur.
  - `RestartManagerProcessInspector`.
  - `MsiProductProbe`.
  - `ShellLinkReader` : résout la cible des `.lnk`.
  - `RecycleBinRemover` : via `IFileOperation`.
  - `RegistryExportBackup` : via `reg export`.
  - `WindowsOfficialUninstaller`.
- **winget**
  - `WinGetCommandBuilder.BuildUninstall` produit `uninstall --id X --exact --source S --silent
    --disable-interactivity --accept-source-agreements [--scope]`.
  - `WinGetProvider` implémente `IPackageUninstallProvider`.
  - `WinGetResultNormalizer` reçoit de nouveaux codes de sortie.

### Stratégie officielle (par priorité)

1. **winget**, si le programme est corrélé à un paquet (collecteur `winget export` existant).
2. **MSI** : `msiexec /x {GUID} /qn /norestart`, avec élévation pour la portée machine.
3. **`QuietUninstallString`**.
4. **Drapeaux selon le type d'installeur** :
   - Inno Setup : `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` ;
   - NSIS : `/S _?=<dir>`, pour attendre la fin.
5. **`UninstallString` interactif**, signalé comme tel dans l'interface.
6. **Steam / Epic** : URI du launcher (P6).

Codes de sortie MSI :

| Code | Signification | Traitement |
|---|---|---|
| 0 | succès | — |
| 1605 | produit absent | no-op |
| 3010 | redémarrage requis | remonté à l'utilisateur |
| 1602 | annulé par l'utilisateur | remonté |
| 1618 | autre installation en cours | réessayer plus tard |

### Desktop

- `ViewModels/Steps/UninstallViewModel.cs` et `Views/UninstallView.axaml`.
- L'onglet est déclaré dans `MainWindowViewModel` et résolu par nom, avec un échec bruyant s'il est
  introuvable (LL-007).
- Les textes vivent dans `Strings/en.axaml` et `Strings/fr.axaml` ; un test vérifie la parité des
  clés.
- Le texte formaté est produit dans le ViewModel.

### Exécutable élevé : `src/AgenStart.Uninstall.Elevated` (P5, console)

- Il est lancé via `runas` **au moment de l'application uniquement**.
- Il ne reçoit que des éléments de portée machine.
- Il **revalide lui-même** chaque élément (`PathSafetyPolicy` et empreintes), sans faire confiance à
  l'appelant.

## 5. Règles déclaratives

### Fichiers et chargement

- Les règles sont dans `docs/product/uninstall-rules.json`, avec leur schéma
  `docs/product/uninstall-rules.schema.json`.
- Elles sont empaquetées comme le catalogue (Content + EmbeddedResource dans
  `AgenStart.Desktop.csproj`).
- Elles sont versionnées par `schemaVersion` et `rulesVersion`.
- Le **plancher de sécurité est codé en dur** dans `PathSafetyPolicy` : la configuration peut
  ajouter des interdits, jamais en retirer.

### Exemple

Les chemins Civilization VII ci-dessous sont **illustratifs**. Ils seront remplacés par ceux relevés
lors de la procédure manuelle.

```json
{
  "schemaVersion": "1.0",
  "rulesVersion": "2026.11.0",
  "extraForbiddenPaths": ["{LocalAppData}\\Steam"],
  "genericNameStoplist": ["common","shared","data","games","launcher","update","cache","temp","microsoft","windows"],
  "genericRules": [
    { "id": "generic.publisher-product", "kind": "directory",
      "roots": ["{LocalAppData}","{RoamingAppData}","{ProgramData}"],
      "relativePath": "{Publisher}\\{Product}", "dataClass": "appData", "confidence": 6 },
    { "id": "generic.product-only", "kind": "directory",
      "roots": ["{LocalAppData}","{RoamingAppData}","{LocalLow}"],
      "relativePath": "{Product}", "dataClass": "appData", "confidence": 3 },
    { "id": "generic.hkcu-publisher-product", "kind": "registryKey",
      "roots": ["HKCU\\Software"], "relativePath": "{Publisher}\\{Product}",
      "dataClass": "configuration", "confidence": 5 }
  ],
  "productRules": [
    { "id": "firaxis.civilization-vii",
      "match": { "publisher": ["Firaxis Games","2K"], "displayName": ["Sid Meier's Civilization VII"] },
      "leftovers": [
        { "kind": "directory", "path": "{LocalAppData}\\Firaxis Games\\Sid Meier's Civilization VII\\Saves", "dataClass": "userData" },
        { "kind": "directory", "path": "{LocalAppData}\\Firaxis Games\\Sid Meier's Civilization VII\\Logs", "dataClass": "cache" },
        { "kind": "directory", "path": "{Documents}\\My Games\\Sid Meier's Civilization VII", "dataClass": "userData" }
      ],
      "removeParentIfEmpty": ["{LocalAppData}\\Firaxis Games"] }
  ]
}
```

### Garde-fous de `PathSafetyPolicy`

Chacun est couvert par un test de propriété.

- **Racines interdites.** Elles ne peuvent être ni une cible, ni l'ancêtre d'une cible :
  - fichiers :
    - racines de lecteur, `%WINDIR%` ;
    - `Program Files`, `Program Files (x86)`, `Common Files` ;
    - `ProgramData`, `Users`, le profil utilisateur ;
    - chaque dossier connu (Documents, Bureau, Local, Roaming, LocalLow, Saved Games, OneDrive)
      et `Temp` ;
    - `WindowsApps`, `Packages`, `%LOCALAPPDATA%\AgenStart` ;
  - registre : `HKLM\SOFTWARE` à une profondeur inférieure à 2, et les clés `Microsoft`,
    `Classes`, `Policies`.
- **Profondeur.**
  - La cible doit être strictement sous une racine autorisée, à une profondeur d'au moins 1.
  - Un dossier au niveau de l'éditeur n'est supprimé que s'il est vide, ou si tous ses enfants sont
    sélectionnés.
- **Correspondance stricte.**
  - Égalité ordinale insensible à la casse, après normalisation (suppression de la version, de
    `(x64)` et des suffixes d'architecture).
  - Pas de sous-chaîne, pas d'expression régulière.
  - Le jeton doit faire au moins 4 caractères et ne pas figurer dans la stoplist.
- **Dossiers partagés exclus.**
  - Cas visé : la cible (ou l'un de ses enfants) correspond à l'éditeur, au nom ou à
    l'`InstallLocation` d'un **autre** logiciel encore installé.
  - Traitement : la cible est exclue (score −100) et affichée « partagé, ignoré ».
- **Liens jamais suivis.** Les points d'analyse (jonctions, liens symboliques) ne sont jamais
  suivis, ni pour le calcul de taille, ni pour la suppression. Les chemins longs passent par
  `\\?\`.
- **OneDrive.** Tout fichier sous OneDrive, y compris les fichiers à la demande, est classé
  `UserData`.
- **Raccourcis.** Un raccourci n'est supprimé que si sa cible résolue est sous l'`InstallLocation`
  supprimée, ou si elle est l'exécutable désinstallé.
- **Niveaux de confiance :**
  - score ≥ 8 : élevé, pré-coché, sauf pour `UserData` ;
  - score de 4 à 7 : moyen, décoché ;
  - score < 4 : non affiché, seulement présent dans le plan JSON exporté.
- **Seuils de volume.** Au-delà d'une taille ou d'un nombre de fichiers fixés en configuration, une
  confirmation supplémentaire est demandée.

### Scores repris de BCU (méthode, pas de code)

| Signal | Poids |
|---|---|
| Clé d'uninstall | +20 |
| Éditeur correspondant | +4 |
| Directement sous un dossier connu | −1 |
| Exécutables présents | −4 |
| Éditeur encore utilisé par un autre logiciel | −4 |
| Dossier encore utilisé | −7 |
| Application Store | −10 |

## 6. Sécurité

### Élévation

- L'application reste `asInvoker`.
- Les éléments de portée utilisateur sont traités sans élévation.
- Les éléments de portée machine passent par **une seule invite UAC**, celle de l'exécutable élevé,
  au moment de l'application et jamais au démarrage.
- winget et les désinstalleurs officiels gèrent leur propre UAC. Seul `msiexec /qn` en portée
  machine exige l'exécutable élevé.
- **Cas de l'élévation par un autre compte administrateur.** Le HKCU et l'AppData de l'exécutable
  élevé sont alors ceux de cet administrateur. L'exécutable élevé ne reçoit donc **jamais**
  d'élément de portée utilisateur.

### Lecture seule et simulation

- P1-P2 sont en lecture seule par construction : les ports n'ont pas de méthode d'écriture.
- À partir de P3, un mode **simulation** produit le journal et le rapport sans appeler
  `ILeftoverRemover` ni `IOfficialUninstaller`.

### Réversibilité

- **Fichiers.**
  - Envoi à la corbeille, après vérification du quota du volume.
  - Un élément non recyclable (trop gros, lecteur réseau ou amovible) devient
    `requires-permanent-delete`.
  - Il n'est supprimé que si l'utilisateur donne un second accord explicite
    (`AllowPermanentDelete`) ; sinon il est ignoré.
- **Registre.** Un `reg export` est fait vers `%LOCALAPPDATA%\AgenStart\uninstall\backups\<sessionId>\`
  avant toute suppression.
- **Tâches planifiées (P5).** Un export XML est fait avant toute suppression.

### Journal

- Fichier `%LOCALAPPDATA%\AgenStart\uninstall\journal\<sessionId>.jsonl`, en ajout seulement.
- Chaque action produit une entrée `intent`, puis une entrée `result`.
- Aucun secret n'y figure : les lignes de commande sont journalisées sans les variables
  d'environnement.
- Les énumérations persistées passent par une correspondance explicite, couverte par un test
  aller-retour (LL-003).

### Idempotence et changements depuis la revue

- **Préconditions.** Chaque action en a une :
  - cible absente → `NoOp` ;
  - entrée ARP absente ou code MSI 1605 → `AlreadyRemoved`.
- **Second passage.** Un second `apply` ne produit **aucune écriture** (test dédié).
- **Changement depuis la revue (TOCTOU).**
  - Chaque élément porte une empreinte : chemin, taille, date de modification, type.
  - Si l'empreinte a changé au moment de l'application, l'élément est ignoré
    (`leftover.changed-since-review`).

### Reprise après interruption

- Un mutex global garantit une seule session de désinstallation à la fois.
- Au lancement, une session présente au journal sans `result` final est signalée « interrompue ».
- Le plan est alors **recalculé** depuis l'état courant, jamais rejoué à l'aveugle : seules les
  actions restantes apparaissent.
- Certains désinstalleurs se relancent dans un autre processus et rendent la main avant la fin
  (Inno `_iu*.tmp`, NSIS `Au_.exe`). Pour eux :
  1. attendre la fin de tout l'arbre de processus ;
  2. puis vérifier la disparition de la clé ARP, avec un délai maximal configurable.

## 7. Phases

Chaque phase doit passer :

- le build ;
- le gate de format (LL-006) ;
- les tests, avec un nombre de tests découverts supérieur à 0 (LL-001) ;
- un démarrage de 15 s (LL-008) ;
- une vérification sur l'état commité (LL-004).

### P0 — Conception (ce document)

- Le présent document et les ADR listés au §8.
- Le workflow `uninstall-tests.yml`, calqué sur `installation-tests.yml`, avec un filtre de chemins
  limité à la surface modifiée.
- Aucun code produit.

### P1 — Inventaire en lecture seule

- **Livrables :**
  - extraction de `UninstallRegistryReader` ;
  - `IInstalledProgramInventory` ;
  - corrélation winget ;
  - onglet et liste.
- **Critères d'acceptation :**
  - les tests `InstalledSoftwareInventoryTests` existants restent verts ;
  - la sortie du collecteur est identique (test de non-régression sur fixture) ;
  - la liste est cohérente avec Paramètres > Applications sur 3 machines ;
  - aucun port d'écriture n'est enregistré dans l'injection de dépendances (test) ;
  - les clés fr/en sont à parité.
- **Tests :**
  - faux registre en mémoire : HKLM 64/32 bits, HKCU, clés illisibles donnant `Partial` ;
  - déduplication ;
  - normalisation des noms.

### P2 — Plan affiché, sans action

- **Livrables :**
  - règles, schéma et chargeur ;
  - `PathSafetyPolicy`, `ConfidenceScorer`, `UninstallPlanner` ;
  - détection des processus ;
  - les 3 sections de l'interface ;
  - export JSON.
- **Critères d'acceptation :**
  - plans « golden » sur des instantanés de machines anonymisés (comme `Recommendations.Tests/Golden`) ;
  - tests de propriété : aucun élément interdit, ancêtre d'une racine interdite, partagé ou atteint
    via un point d'analyse ;
  - `UserData` n'est jamais pré-coché ;
  - zéro écriture.
- **Tests :**
  - `InMemoryFileSystemProbe` : jonctions, chemins longs, OneDrive ;
  - `FakeKnownFolderResolver` : dossiers redirigés ou localisés ;
  - le chargeur rejette un schéma invalide ou inconnu.

### P3 — Désinstallation officielle

- **Livrables :**
  - `IPackageUninstallProvider` dans `WinGetProvider`, et `BuildUninstall` ;
  - `OfficialStrategySelector`, `WindowsOfficialUninstaller` ;
  - fermeture des processus après accord ;
  - `UninstallSession`, vérification, journal, rapport.
- **Activation :** derrière un **drapeau expérimental**, jusqu'à ce que la campagne sur machine
  jetable soit verte.
- **Critères d'acceptation :**
  - chaque stratégie est couverte ;
  - un second passage donne `AlreadyRemoved` ;
  - les codes 1618, 3010 et 1602 sont correctement remontés.
- **Tests :**
  - unitaires : `WinGetCommandBuilderTests` et le normaliseur, avec un faux exécuteur ;
  - **sur machine jetable** : Windows Sandbox (`.wsb`, gratuit sur Windows 11 Pro), et un job
    `workflow_dispatch` sur `windows-latest` (machine virtuelle éphémère) ;
  - jeu de logiciels : 7-Zip (MSI), Notepad++ (NSIS), une application Inno, VS Code en
    installation utilisateur.

### P4 — Restes de portée utilisateur (fichiers, raccourcis) vers la corbeille

- **Livrables :**
  - `RecycleBinRemover`, `ShellLinkReader` ;
  - re-vérification des empreintes ;
  - reprise après interruption.
- **Critères d'acceptation :**
  - apply puis apply = zéro écriture ;
  - après un arrêt en cours de route, le nouveau plan ne contient que le reste ;
  - un dépassement de quota ne déclenche aucune suppression ;
  - les données utilisateur restent intactes sur la machine jetable.

### P5 — Restes de portée machine (registre, tâches, pare-feu)

- **Livrables :**
  - exécutable élevé avec revalidation ;
  - `reg export`, puis suppression ;
  - tâches planifiées : paquet NuGet TaskScheduler (MIT), en Adopt après vérification ;
  - règles de pare-feu via `INetFwPolicy2`.
- **Critères d'acceptation :**
  - l'exécutable élevé refuse tout élément de portée utilisateur ou hors politique (tests) ;
  - une sauvegarde est restaurable sur la machine jetable.

### P6 (optionnel)

- MSIX / Appx : via winget, ou ADR sur le TFM.
- Orphelins de `Program Files`.
- Launchers Steam et Epic.

À chaque phase : entrée PASSATION (en ajout seulement), CHANGELOG, et section « RAIDER review »
dans la PR.

## 8. Risques, questions ouvertes, ADR

### Risques

| Risque | Atténuation |
|---|---|
| Faux positif, donc perte de données | Garde-fous §5, corbeille, plan revu, `UserData` jamais pré-coché |
| Suppression définitive silencieuse quand le quota de la corbeille est dépassé | Vérification préalable du quota, second accord explicite |
| Élévation par un autre compte administrateur (mauvais HKCU) | L'exécutable élevé ne reçoit que des éléments de portée machine |
| Fichiers OneDrive à la demande : la suppression se propage au cloud | Tout fichier OneDrive est classé `UserData` |
| Désinstalleur qui rend la main trop tôt, ouvre une page web ou propose des offres | Attente de l'arbre de processus et de la disparition de la clé ARP ; signalement dans l'interface |
| Redémarrage en attente (3010) avec des fichiers verrouillés | Le plan propose « après redémarrage » |
| Binaire non signé (signature reportée) qui supprime dans AppData : heuristique antivirus ou SmartScreen | Tests sur machine jetable ; à suivre avec la décision sur la signature |
| Dossiers connus redirigés ou localisés | `SHGetKnownFolderPath`, jamais de chemin en dur |
| Règles d'éditeur devenues obsolètes | Le plancher codé reste prioritaire ; règles versionnées |

### Questions ouvertes

1. Les sessions de désinstallation doivent-elles apparaître dans `history.json` ? Si oui, le format
   doit rester rétrocompatible (LL-009).
2. « Effacer les données locales » doit-il supprimer le journal et les sauvegardes `.reg` ?
   Recommandation : non, ou alors avec une confirmation séparée.
3. Quel mécanisme pour le drapeau expérimental de P3 et des phases suivantes ? Si c'est un champ de
   paramètres, il faut choisir sa valeur par défaut pour les données existantes (LL-009).
4. Comment les règles sont-elles maintenues (contributions, fréquence) ? La v1 ne prévoit que des
   règles livrées avec l'application.
5. Steam et Epic : URI du launcher, ou exclusion ?

### ADR

Proposés (statut *Proposed*) :

1. [ADR-0004](../adr/0004-uninstall-execution-and-elevation.md) — modèle d'exécution et
   d'élévation.
   - Les chaînes de désinstallation lues dans le registre sont **analysées, jamais passées à un
     shell**.
   - Elles sont refusées hors des racines autorisées, ce qui préserve ADR-0002 §3-§4.
   - L'exécutable élevé est ponctuel, revalide chaque élément et n'accepte que la portée machine.
2. [ADR-0005](../adr/0005-uninstall-deletion-semantics.md) — sémantique de suppression.
   - Corbeille avec vérification préalable du quota.
   - `reg export` avant toute suppression de registre.
   - Suppression définitive seulement après un second accord.
3. [ADR-0006](../adr/0006-uninstall-rules-and-safety-floor.md) — schéma des règles ; la
   configuration ne peut que durcir le plancher codé en dur.
4. [ADR-0007](../adr/0007-uninstall-journal-format.md) — format du journal JSONL et
   correspondance des énumérations persistées (LL-003).

À rédiger seulement si le cas se présente :

5. *(si P6 passe par `PackageManager`)* passage au TFM `net10.0-windows10.0.19041`.
6. *(si du code BCU est porté)* NOTICE Apache-2.0.

## 9. Revue RAIDER

| Principe | Couverture |
|---|---|
| **R** Reusable | Règles déclaratives ; ports réutilisables ; capacité `IPackageUninstallProvider` optionnelle |
| **A** Agnostic | Dossiers connus résolus à l'exécution ; cœur indépendant de Windows ; adaptateurs séparés |
| **I** Idempotent | Préconditions par action, empreintes, recalcul du plan à la reprise ; second `apply` = zéro écriture |
| **D** Durable | `IPackageProvider` et `InstalledSoftwareRecord` inchangés ; non-régression du collecteur |
| **Failure memory** | LL-001, LL-003, LL-004, LL-006, LL-007, LL-008 et LL-009 appliqués dans les critères des phases |
| **E** Engineering-grade | `plan → revue → application`, moindre privilège, journal sans secrets, comparatif reuse-first (§2) |
| **Scoped execution** | `uninstall-tests.yml` avec un filtre de chemins limité au module |
| **R** Retroactive | Ne supprime que ce qui est listé et coché ; rien de non géré n'est touché |
