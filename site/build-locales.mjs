import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const siteDirectory = path.dirname(fileURLToPath(import.meta.url));
const sourcePath = path.join(siteDirectory, "index.html");
const frenchDirectory = path.join(siteDirectory, "fr");
const frenchPath = path.join(frenchDirectory, "index.html");

let french = await readFile(sourcePath, "utf8");

function replaceRequired(search, replacement) {
  if (!french.includes(search)) {
    throw new Error(`French landing source marker not found: ${search}`);
  }
  french = french.replaceAll(search, replacement);
}

const replacements = [
  ['<html lang="en">', '<html lang="fr">'],
  [
    'AgenStart is a local-first Windows setup assistant that understands your PC, recommends the right tools, and installs them through trusted providers.',
    'AgenStart est un assistant Windows local-first qui comprend votre PC, recommande les bons outils et les installe via des sources fiables.',
  ],
  [
    'AgenStart — Prepare a Windows PC without the scavenger hunt',
    'AgenStart — Préparez un PC Windows sans chasse aux logiciels',
  ],
  [
    'Understand the machine. Build the right setup. Install through trusted providers. Save the result.',
    'Comprendre la machine. Construire le bon setup. Installer via des sources fiables. Conserver le résultat.',
  ],
  ['https://eaglefox31.github.io/AgenStart/" />\n  <meta name="twitter:card"', 'https://eaglefox31.github.io/AgenStart/fr/" />\n  <meta name="twitter:card"'],
  [
    '<link rel="canonical" href="https://eaglefox31.github.io/AgenStart/" />',
    '<link rel="canonical" href="https://eaglefox31.github.io/AgenStart/fr/" />',
  ],
  ['AgenStart — A smarter Windows setup', 'AgenStart — Un setup Windows plus intelligent'],
  ['Skip to content', 'Aller au contenu'],
  ['aria-label="AgenStart home"', 'aria-label="Accueil AgenStart"'],
  ['by AgenStudio', 'par AgenStudio'],
  ['aria-label="Primary navigation"', 'aria-label="Navigation principale"'],
  ['>Product</a>', '>Produit</a>'],
  ['>Flow</a>', '>Parcours</a>'],
  ['>Trust</a>', '>Confiance</a>'],
  ['aria-label="Language"', 'aria-label="Langue"'],
  [
    '<a class="is-active" href="./" lang="en" aria-current="page">EN</a>\n        <a href="./fr/" lang="fr">FR</a>',
    '<a href="../" lang="en">EN</a>\n        <a class="is-active" href="./" lang="fr" aria-current="page">FR</a>',
  ],
  ['>Download</a>', '>Télécharger</a>'],
  ['Windows 10/11 · local-first · no account required', 'Windows 10/11 · local-first · aucun compte requis'],
  [
    'Your next PC setup<br />\n          should already know<br />\n          <span>what you need.</span>',
    'Votre prochain setup PC<br />\n          devrait déjà savoir<br />\n          <span>ce qu’il vous faut.</span>',
  ],
  [
    'AgenStart understands the machine, understands the job, and turns a fresh Windows install into a ready-to-work setup — without hunting down every app yourself.',
    'AgenStart comprend la machine, comprend l’usage prévu et transforme une installation Windows fraîche en poste prêt à travailler — sans chercher chaque application une par une.',
  ],
  ['Download for Windows', 'Télécharger pour Windows'],
  ['Latest stable', 'Dernière version stable'],
  ['See how it works', 'Voir comment ça marche'],
  ['aria-label="Product principles"', 'aria-label="Principes produit"'],
  ['Trusted providers', 'Sources fiables'],
  ['Explainable recommendations', 'Recommandations explicables'],
  ['Reproducible setups', 'Setups reproductibles'],
  ['aria-label="AgenStart product preview"', 'aria-label="Aperçu du produit AgenStart"'],
  ['Overview', 'Vue d’ensemble'],
  ['Machine', 'Machine'],
  ['Recommendations', 'Recommandations'],
  ['Install', 'Installer'],
  ['History', 'Historique'],
  ['SMART SETUP', 'SETUP INTELLIGENT'],
  ['Development workstation', 'Poste de développement'],
  ['Machine ready', 'Machine prête'],
  ['Memory', 'Mémoire'],
  ['High capability', 'Haute capacité'],
  ['Native packages', 'Packages natifs'],
  ['RECOMMENDED STACK', 'STACK RECOMMANDÉ'],
  ['14 apps fit this machine + profile', '14 apps adaptées à cette machine et ce profil'],
  ['8 already installed', '8 déjà installées'],
  ['Core development tool', 'Outil de développement essentiel'],
  ['Recommended for Development', 'Recommandé pour le développement'],
  ['Machine supports virtualization', 'Machine compatible avec la virtualisation'],
  ['Smart match', 'Correspondance idéale'],
  ['Already available', 'Déjà disponible'],
  ['Installed', 'Installé'],
  ['Ready', 'Prêt'],
  ['Skip', 'Ignorer'],
  ['Trusted source', 'Source fiable'],
  ['WinGet verified', 'Vérifié via WinGet'],
  ['Reusable setup', 'Setup réutilisable'],
  ['Export when done', 'Exportable une fois terminé'],
  ['Scroll to unpack the setup', 'Faites défiler pour découvrir le setup'],
  ['01 / THE IDEA', '01 / L’IDÉE'],
  [
    'AgenStart doesn’t give you <em>a list.</em><br />It gives you <span>a setup.</span>',
    'AgenStart ne vous donne pas <em>une liste.</em><br />Il vous donne <span>un setup.</span>',
  ],
  [
    'Fresh PC setups are still built like scavenger hunts: remember every tool, find the official source, install it, repeat it, then do the whole thing again on the next machine.',
    'Configurer un PC neuf ressemble encore trop souvent à une chasse au trésor : se souvenir de chaque outil, retrouver la source officielle, installer, recommencer, puis refaire exactement la même chose sur la machine suivante.',
  ],
  [
    'AgenStart compresses that work into one deliberate flow. It reads the machine, understands your intended use, explains what it recommends, lets you approve the plan, then installs through trusted providers.',
    'AgenStart concentre ce travail dans un parcours cohérent. Il analyse la machine, comprend l’usage prévu, explique ses recommandations, vous laisse valider le plan, puis installe via des sources fiables.',
  ],
  ['aria-label="AgenStart product capabilities"', 'aria-label="Capacités produit AgenStart"'],
  ['Machine-aware', 'Adapté à la machine'],
  ['Recommendations match the hardware.', 'Les recommandations tiennent compte du matériel.'],
  ['Profile-aware', 'Adapté au profil'],
  ['Development ≠ Creation ≠ Business.', 'Développement ≠ Création ≠ Entreprise.'],
  ['Your inventory stays on your machine.', 'Votre inventaire reste sur votre machine.'],
  ['Explainable', 'Explicable'],
  ['Every recommendation has a reason.', 'Chaque recommandation est justifiée.'],
  ['02 / ONE DELIBERATE FLOW', '02 / UN PARCOURS COHÉRENT'],
  ['From “fresh Windows”<br />to “ready to work.”', 'De « Windows tout neuf »<br />à « prêt à travailler ».'],
  [
    'Five stages. One coherent setup. Nothing changes on the machine until you approve it.',
    'Cinq étapes. Un setup cohérent. Rien ne change sur la machine avant votre validation.',
  ],
  ['UNDERSTAND THE PC', 'COMPRENDRE LE PC'],
  ['See the machine before suggesting software.', 'Comprendre la machine avant de proposer des logiciels.'],
  [
    'OS, architecture, memory, graphics capability and installed apps become useful context — not a fingerprint.',
    'Système, architecture, mémoire, capacités graphiques et applications installées deviennent un contexte utile — pas une empreinte.',
  ],
  ['UNDERSTAND THE GOAL', 'COMPRENDRE LE BESOIN'],
  ['Tell AgenStart what this machine is for.', 'Indiquez à AgenStart l’usage prévu de cette machine.'],
  [
    'The intended use shapes the setup. A development machine needs a different stack from a classroom PC or a creator workstation.',
    'L’usage prévu façonne le setup. Une machine de développement n’a pas besoin de la même stack qu’un PC de formation ou qu’un poste de création.',
  ],
  ['Development', 'Développement'],
  ['Business', 'Entreprise'],
  ['Training', 'Formation'],
  ['Personal', 'Personnel'],
  ['BUILD THE STACK', 'CONSTRUIRE LA STACK'],
  ['Get recommendations that can explain themselves.', 'Obtenez des recommandations capables de se justifier.'],
  [
    'No “because AI said so.” AgenStart tells you why an app belongs in the setup, what is already installed and what can be skipped.',
    'Pas de « parce que l’IA l’a dit ». AgenStart explique pourquoi une application a sa place dans le setup, ce qui est déjà installé et ce qui peut être ignoré.',
  ],
  ['PowerToys · utility', 'PowerToys · utilitaire'],
  ['VS Code · smart match', 'VS Code · correspondance idéale'],
  ['INSTALL SAFELY', 'INSTALLER EN SÉCURITÉ'],
  ['Approve once. Watch the setup assemble itself.', 'Validez une fois. Regardez le setup se construire.'],
  [
    'Packages are prepared and installed through trusted provider paths with real progress, clear failures and safe retry behavior.',
    'Les packages sont préparés et installés via des sources fiables, avec une progression réelle, des erreurs explicites et des relances sûres.',
  ],
  ['Installing', 'Installation'],
  ['Queued', 'En attente'],
  ['REPRODUCE IT', 'LE REPRODUIRE'],
  ['Keep the result, not just the memory of it.', 'Conservez le résultat, pas seulement le souvenir du setup.'],
  [
    'Export the setup so the next rebuild or compatible machine can start from a known recipe instead of another blank slate.',
    'Exportez le setup pour que la prochaine réinstallation ou machine compatible reparte d’une recette connue plutôt que d’une page blanche.',
  ],
  ['03 / CURATED, NOT CROWDED', '03 / SÉLECTIONNÉ, PAS SURCHARGÉ'],
  ['The tools you actually need.<br />Not a catalogue of everything.', 'Les outils dont vous avez vraiment besoin.<br />Pas un catalogue de tout.'],
  [
    'AgenStart favors a maintained, explainable catalogue over hundreds of random installers.',
    'AgenStart privilégie un catalogue maintenu et explicable plutôt que des centaines d’installateurs choisis au hasard.',
  ],
  ['04 / TRUST IS PART OF THE PRODUCT', '04 / LA CONFIANCE FAIT PARTIE DU PRODUIT'],
  [
    'It should be easier to install software — not easier to lose control of your machine.',
    'Installer des logiciels doit devenir plus simple — pas perdre le contrôle de sa machine.',
  ],
  [
    'AgenStart is designed around explicit approval, trusted package providers and local machine analysis. It does not need your browser history, personal files, passwords, MAC address or device serial number to recommend a setup.',
    'AgenStart repose sur une validation explicite, des sources de packages fiables et une analyse locale de la machine. Il n’a pas besoin de votre historique de navigation, de vos fichiers personnels, de vos mots de passe, de votre adresse MAC ou du numéro de série de l’appareil pour recommander un setup.',
  ],
  ['Read the product principles', 'Lire les principes produit'],
  ['Local-first analysis', 'Analyse local-first'],
  ['Machine inventory is useful context, not a cloud dependency.', 'L’inventaire de la machine sert de contexte, pas de dépendance au cloud.'],
  ['You approve the plan', 'Vous validez le plan'],
  ['Detection can be automatic. Installation is never silent.', 'La détection peut être automatique. L’installation ne se fait jamais en silence.'],
  ['Trusted package paths', 'Sources de packages fiables'],
  ['WinGet first, controlled official-source fallbacks where needed.', 'WinGet en priorité, avec des sources officielles contrôlées en secours si nécessaire.'],
  ['Failure stays visible', 'Les échecs restent visibles'],
  ['A failed install is reported as failed — with retry where safe.', 'Une installation échouée reste signalée comme telle — avec relance lorsqu’elle est sûre.'],
  ['READY WHEN THE MACHINE IS', 'PRÊT QUAND LA MACHINE L’EST'],
  ['Stop rebuilding your setup from memory.', 'Arrêtez de reconstruire votre setup de mémoire.'],
  [
    'Download AgenStart, review what it recommends, and turn the next fresh Windows install into something deliberate.',
    'Téléchargez AgenStart, vérifiez ses recommandations et transformez votre prochaine installation Windows en setup réfléchi.',
  ],
  ['Download AgenStart', 'Télécharger AgenStart'],
  ['View on GitHub', 'Voir sur GitHub'],
  ['release details fetched from GitHub', 'détails de version récupérés depuis GitHub'],
  ['An AgenStudio project by EagleFox31', 'Un projet AgenStudio par EagleFox31'],
  ['Releases', 'Versions'],
  ['Docs', 'Documentation'],
];

for (const [search, replacement] of replacements) {
  replaceRequired(search, replacement);
}

replaceRequired('href="./styles.css"', 'href="../styles.css"');
replaceRequired('src="./app.js"', 'src="../app.js"');
replaceRequired('src="./assets/generated-logos/', 'src="../assets/generated-logos/');

await mkdir(frenchDirectory, { recursive: true });
await writeFile(frenchPath, french);

console.log("Generated French landing page at site/fr/index.html");
