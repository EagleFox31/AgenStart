const RELEASE_API = "https://api.github.com/repos/EagleFox31/AgenStart/releases/latest";
const RELEASES_FALLBACK = "https://github.com/EagleFox31/AgenStart/releases/latest";
const LANGUAGE_STORAGE_KEY = "agenstart-language";

const META = {
  en: {
    title: "AgenStart — A smarter Windows setup",
    description: "AgenStart is a local-first Windows setup assistant that understands your PC, recommends the right tools, and installs them through trusted providers.",
    ogTitle: "AgenStart — Prepare a Windows PC without the scavenger hunt",
    ogDescription: "Understand the machine. Build the right setup. Install through trusted providers. Save the result.",
    locale: "en_US",
  },
  fr: {
    title: "AgenStart — Une configuration Windows plus intelligente",
    description: "AgenStart est un assistant Windows local-first qui comprend votre PC, recommande les bons outils et les installe depuis des sources de confiance.",
    ogTitle: "AgenStart — Préparez un PC Windows sans chercher chaque logiciel",
    ogDescription: "Comprendre la machine. Construire la bonne configuration. Installer depuis des sources de confiance. Réutiliser le résultat.",
    locale: "fr_FR",
  },
};

const FRENCH = new Map([
  ["Skip to content", "Aller au contenu"],
  ["by AgenStudio", "par AgenStudio"],
  ["Product", "Produit"],
  ["Flow", "Parcours"],
  ["Trust", "Confiance"],
  ["Download", "Télécharger"],
  ["Windows 10/11 · local-first · no account required", "Windows 10/11 · local-first · aucun compte requis"],
  ["Your next PC setup", "Votre prochaine configuration PC"],
  ["should already know", "devrait déjà savoir"],
  ["what you need.", "ce qu’il vous faut."],
  ["AgenStart understands the machine, understands the job, and turns a fresh Windows install into a ready-to-work setup — without hunting down every app yourself.", "AgenStart comprend la machine, comprend son usage et transforme une nouvelle installation Windows en poste prêt à travailler — sans vous obliger à rechercher chaque logiciel un par un."],
  ["Download for Windows", "Télécharger pour Windows"],
  ["Latest stable", "Dernière version stable"],
  ["See how it works", "Voir comment ça marche"],
  ["Trusted providers", "Sources de confiance"],
  ["Explainable recommendations", "Recommandations expliquées"],
  ["Reproducible setups", "Configurations reproductibles"],
  ["Overview", "Aperçu"],
  ["Machine", "Machine"],
  ["Recommendations", "Recommandations"],
  ["Install", "Installation"],
  ["History", "Historique"],
  ["SMART SETUP", "CONFIGURATION INTELLIGENTE"],
  ["Development workstation", "Poste de développement"],
  ["Machine ready", "Machine prête"],
  ["OS", "OS"],
  ["Memory", "Mémoire"],
  ["Compatible", "Compatible"],
  ["High capability", "Haute capacité"],
  ["Architecture", "Architecture"],
  ["Native packages", "Paquets natifs"],
  ["RECOMMENDED STACK", "ENVIRONNEMENT RECOMMANDÉ"],
  ["14 apps fit this machine + profile", "14 apps adaptées à cette machine + ce profil"],
  ["8 already installed", "8 déjà installées"],
  ["Core development tool", "Outil de développement essentiel"],
  ["Installed", "Installé"],
  ["Recommended for Development", "Recommandé pour le développement"],
  ["Ready", "Prêt"],
  ["Machine supports virtualization", "Machine compatible avec la virtualisation"],
  ["Smart match", "Correspondance idéale"],
  ["Already available", "Déjà disponible"],
  ["Skip", "Ignorer"],
  ["Trusted source", "Source de confiance"],
  ["WinGet verified", "Vérifié via WinGet"],
  ["Reusable setup", "Configuration réutilisable"],
  ["Export when done", "Exporter à la fin"],
  ["Scroll to unpack the setup", "Faites défiler pour découvrir la configuration"],
  ["01 / THE IDEA", "01 / L’IDÉE"],
  ["AgenStart doesn’t give you", "AgenStart ne vous donne pas"],
  ["a list.", "une liste."],
  ["It gives you", "Il vous donne"],
  ["a setup.", "une configuration."],
  ["Fresh PC setups are still built like scavenger hunts: remember every tool, find the official source, install it, repeat it, then do the whole thing again on the next machine.", "Configurer un nouveau PC ressemble encore trop souvent à une chasse au trésor : se souvenir de chaque outil, retrouver la source officielle, l’installer, recommencer, puis refaire tout le processus sur la machine suivante."],
  ["AgenStart compresses that work into one deliberate flow. It reads the machine, understands your intended use, explains what it recommends, lets you approve the plan, then installs through trusted providers.", "AgenStart rassemble ce travail dans un parcours cohérent. Il analyse la machine, comprend l’usage prévu, explique ses recommandations, vous laisse valider le plan, puis installe depuis des sources de confiance."],
  ["Machine-aware", "Adapté à la machine"],
  ["Recommendations match the hardware.", "Les recommandations tiennent compte du matériel."],
  ["Profile-aware", "Adapté au profil"],
  ["Development ≠ Creation ≠ Business.", "Développement ≠ Création ≠ Usage professionnel."],
  ["Local-first", "Local-first"],
  ["Your inventory stays on your machine.", "L’inventaire reste sur votre machine."],
  ["Explainable", "Explicable"],
  ["Every recommendation has a reason.", "Chaque recommandation a une raison."],
  ["02 / ONE DELIBERATE FLOW", "02 / UN PARCOURS COHÉRENT"],
  ["From “fresh Windows”", "De « Windows tout neuf »"],
  ["to “ready to work.”", "à « prêt à travailler »."],
  ["Five stages. One coherent setup. Nothing changes on the machine until you approve it.", "Cinq étapes. Une configuration cohérente. Rien n’est modifié sur la machine avant votre validation."],
  ["UNDERSTAND THE PC", "COMPRENDRE LE PC"],
  ["See the machine before suggesting software.", "Comprendre la machine avant de proposer des logiciels."],
  ["OS, architecture, memory, graphics capability and installed apps become useful context — not a fingerprint.", "Le système, l’architecture, la mémoire, les capacités graphiques et les apps installées servent de contexte utile — pas d’empreinte numérique."],
  ["UNDERSTAND THE GOAL", "COMPRENDRE L’USAGE"],
  ["Development", "Développement"],
  ["Business", "Professionnel"],
  ["Creation", "Création"],
  ["Training", "Formation"],
  ["Personal", "Personnel"],
  ["Tell AgenStart what this machine is for.", "Dites à AgenStart à quoi servira cette machine."],
  ["The intended use shapes the setup. A development machine needs a different stack from a classroom PC or a creator workstation.", "L’usage prévu façonne la configuration. Un poste de développement n’a pas besoin du même environnement qu’un PC de formation ou une station de création."],
  ["BUILD THE STACK", "CONSTRUIRE L’ENVIRONNEMENT"],
  ["PowerToys · utility", "PowerToys · utilitaire"],
  ["Docker · compatible", "Docker · compatible"],
  ["VS Code · smart match", "VS Code · choix pertinent"],
  ["Get recommendations that can explain themselves.", "Obtenez des recommandations capables de s’expliquer."],
  ["No “because AI said so.” AgenStart tells you why an app belongs in the setup, what is already installed and what can be skipped.", "Pas de « parce que l’IA l’a dit ». AgenStart explique pourquoi une app a sa place dans la configuration, ce qui est déjà installé et ce qui peut être ignoré."],
  ["INSTALL SAFELY", "INSTALLER EN TOUTE MAÎTRISE"],
  ["Installing", "Installation"],
  ["Queued", "En attente"],
  ["Approve once. Watch the setup assemble itself.", "Validez une fois. Regardez la configuration se mettre en place."],
  ["Packages are prepared and installed through trusted provider paths with real progress, clear failures and safe retry behavior.", "Les paquets sont préparés et installés via des sources de confiance, avec une progression réelle, des échecs clairement signalés et des reprises sûres."],
  ["REPRODUCE IT", "LA REPRODUIRE"],
  ["Keep the result, not just the memory of it.", "Conservez le résultat, pas seulement le souvenir de la configuration."],
  ["Export the setup so the next rebuild or compatible machine can start from a known recipe instead of another blank slate.", "Exportez la configuration pour que la prochaine réinstallation ou machine compatible reparte d’une recette connue plutôt que de zéro."],
  ["03 / CURATED, NOT CROWDED", "03 / SÉLECTIONNÉ, PAS SURCHARGÉ"],
  ["The tools you actually need.", "Les outils dont vous avez réellement besoin."],
  ["Not a catalogue of everything.", "Pas un catalogue de tout ce qui existe."],
  ["AgenStart favors a maintained, explainable catalogue over hundreds of random installers.", "AgenStart privilégie un catalogue maintenu et explicable plutôt que des centaines d’installateurs choisis au hasard."],
  ["04 / TRUST IS PART OF THE PRODUCT", "04 / LA CONFIANCE FAIT PARTIE DU PRODUIT"],
  ["It should be easier to install software — not easier to lose control of your machine.", "Installer des logiciels doit devenir plus simple — pas perdre le contrôle de sa machine."],
  ["AgenStart is designed around explicit approval, trusted package providers and local machine analysis. It does not need your browser history, personal files, passwords, MAC address or device serial number to recommend a setup.", "AgenStart repose sur une validation explicite, des sources de paquets fiables et une analyse locale de la machine. Il n’a pas besoin de votre historique de navigation, de vos fichiers personnels, de vos mots de passe, de votre adresse MAC ni du numéro de série de l’appareil pour recommander une configuration."],
  ["Read the product principles", "Lire les principes du produit"],
  ["Local-first analysis", "Analyse local-first"],
  ["Machine inventory is useful context, not a cloud dependency.", "L’inventaire de la machine sert de contexte utile, pas de dépendance au cloud."],
  ["You approve the plan", "Vous validez le plan"],
  ["Detection can be automatic. Installation is never silent.", "La détection peut être automatique. L’installation ne se fait jamais en silence."],
  ["Trusted package paths", "Sources de paquets fiables"],
  ["WinGet first, controlled official-source fallbacks where needed.", "WinGet en priorité, avec des sources officielles contrôlées lorsque nécessaire."],
  ["Failure stays visible", "Les échecs restent visibles"],
  ["A failed install is reported as failed — with retry where safe.", "Une installation échouée reste signalée comme telle — avec reprise lorsque c’est sûr."],
  ["READY WHEN THE MACHINE IS", "PRÊT QUAND LA MACHINE L’EST"],
  ["Stop rebuilding your setup from memory.", "Arrêtez de reconstruire votre environnement de mémoire."],
  ["Download AgenStart, review what it recommends, and turn the next fresh Windows install into something deliberate.", "Téléchargez AgenStart, examinez ses recommandations et transformez votre prochaine installation Windows en configuration maîtrisée."],
  ["Download AgenStart", "Télécharger AgenStart"],
  ["View on GitHub", "Voir sur GitHub"],
  ["Windows 10/11 x64 · release details fetched from GitHub", "Windows 10/11 x64 · détails de version récupérés depuis GitHub"],
  ["An AgenStudio project by EagleFox31", "Un projet AgenStudio par EagleFox31"],
  ["Releases", "Versions"],
  ["Docs", "Docs"],
]);

const header = document.querySelector("[data-header]");
const languageToggle = document.querySelector("[data-language-toggle]");
const releaseLinks = [...document.querySelectorAll("[data-download-link]")];
const releaseVersionLabels = [...document.querySelectorAll("[data-release-version]")];
const releaseNote = document.querySelector("[data-release-note]");
const originalText = new WeakMap();

let currentLanguage = "en";
let latestRelease = null;

const setHeaderState = () => {
  header?.classList.toggle("is-scrolled", window.scrollY > 18);
};

function getInitialLanguage() {
  const parameter = new URLSearchParams(window.location.search).get("lang");
  if (parameter === "fr" || parameter === "en") return parameter;

  try {
    const stored = window.localStorage.getItem(LANGUAGE_STORAGE_KEY);
    if (stored === "fr" || stored === "en") return stored;
  } catch {
    // Storage is optional; the page still works without it.
  }

  return "en";
}

function translateTextNodes(language) {
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  let node = walker.nextNode();

  while (node) {
    const parent = node.parentElement;
    if (!parent || parent.matches("script, style, [data-language-toggle], [data-release-version], [data-release-note]")) {
      node = walker.nextNode();
      continue;
    }

    if (!originalText.has(node)) originalText.set(node, node.nodeValue ?? "");

    const source = originalText.get(node) ?? "";
    const core = source.trim();

    if (core) {
      const leading = source.match(/^\s*/)?.[0] ?? "";
      const trailing = source.match(/\s*$/)?.[0] ?? "";
      const translated = language === "fr" ? (FRENCH.get(core) ?? core) : core;
      node.nodeValue = `${leading}${translated}${trailing}`;
    }

    node = walker.nextNode();
  }
}

function updateMetadata(language) {
  const meta = META[language];
  document.documentElement.lang = language;
  document.title = meta.title;
  document.querySelector('meta[name="description"]')?.setAttribute("content", meta.description);
  document.querySelector('meta[property="og:title"]')?.setAttribute("content", meta.ogTitle);
  document.querySelector('meta[property="og:description"]')?.setAttribute("content", meta.ogDescription);
  document.querySelector('meta[property="og:locale"]')?.setAttribute("content", meta.locale);
}

function updateAccessibility(language) {
  const values = language === "fr"
    ? {
        nav: "Navigation principale",
        brand: "Accueil AgenStart",
        hero: "Aperçu du produit AgenStart",
        capabilities: "Fonctionnalités principales d’AgenStart",
        heroProof: "Principes du produit",
        switcher: "Passer à l’anglais",
      }
    : {
        nav: "Primary navigation",
        brand: "AgenStart home",
        hero: "AgenStart product preview",
        capabilities: "AgenStart product capabilities",
        heroProof: "Product principles",
        switcher: "Passer au français",
      };

  document.querySelector(".nav")?.setAttribute("aria-label", values.nav);
  document.querySelector(".brand")?.setAttribute("aria-label", values.brand);
  document.querySelector(".hero-stage")?.setAttribute("aria-label", values.hero);
  document.querySelector(".proof-strip")?.setAttribute("aria-label", values.capabilities);
  document.querySelector(".hero-proof")?.setAttribute("aria-label", values.heroProof);
  languageToggle?.setAttribute("aria-label", values.switcher);
}

function renderRelease() {
  const version = latestRelease?.version ?? (currentLanguage === "fr" ? "Dernière version stable" : "Latest stable");
  const targetUrl = latestRelease?.targetUrl ?? RELEASES_FALLBACK;

  releaseLinks.forEach((link) => {
    link.href = targetUrl;
    link.setAttribute(
      "aria-label",
      currentLanguage === "fr"
        ? `Télécharger AgenStart ${version} pour Windows`
        : `Download AgenStart ${version} for Windows`,
    );
  });

  releaseVersionLabels.forEach((label) => {
    label.textContent = version;
  });

  if (releaseNote) {
    if (latestRelease) {
      releaseNote.textContent = ["Windows 10/11 x64", latestRelease.version, latestRelease.size]
        .filter(Boolean)
        .join(" · ");
    } else {
      releaseNote.textContent = currentLanguage === "fr"
        ? "Windows 10/11 x64 · détails de version récupérés depuis GitHub"
        : "Windows 10/11 x64 · release details fetched from GitHub";
    }
  }
}

function updateUrl(language) {
  const url = new URL(window.location.href);
  url.searchParams.set("lang", language);
  window.history.replaceState({}, "", url);
}

function applyLanguage(language, { updateHistory = true } = {}) {
  currentLanguage = language;
  translateTextNodes(language);
  updateMetadata(language);
  updateAccessibility(language);
  renderRelease();

  if (languageToggle) {
    languageToggle.dataset.language = language;
  }

  try {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
  } catch {
    // Storage is optional.
  }

  if (updateHistory) updateUrl(language);
}

setHeaderState();
window.addEventListener("scroll", setHeaderState, { passive: true });

document.querySelector("[data-year]").textContent = new Date().getFullYear();

languageToggle?.addEventListener("click", () => {
  applyLanguage(currentLanguage === "en" ? "fr" : "en");
});

applyLanguage(getInitialLanguage(), { updateHistory: false });

if (!(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches)) {
  const observer = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        }
      });
    },
    { rootMargin: "0px 0px -8% 0px", threshold: 0.12 },
  );

  document.querySelectorAll(".reveal").forEach((element) => observer.observe(element));
} else {
  document.querySelectorAll(".reveal").forEach((element) => element.classList.add("is-visible"));
}

async function resolveLatestRelease() {
  try {
    const response = await fetch(RELEASE_API, {
      headers: { Accept: "application/vnd.github+json" },
    });

    if (!response.ok) throw new Error(`GitHub API returned ${response.status}`);

    const release = await response.json();
    const asset = release.assets?.find((candidate) =>
      /AgenStart-.*-win-x64\.zip$/i.test(candidate.name),
    );

    latestRelease = {
      targetUrl: asset?.browser_download_url || release.html_url || RELEASES_FALLBACK,
      version: release.tag_name || (currentLanguage === "fr" ? "Dernière version stable" : "Latest stable"),
      size: asset?.size ? `${Math.round(asset.size / 1024 / 1024)} MB` : null,
    };

    renderRelease();
  } catch (error) {
    console.info("AgenStart release metadata unavailable; using the stable releases URL.", error);
    latestRelease = null;
    renderRelease();
  }
}

resolveLatestRelease();
