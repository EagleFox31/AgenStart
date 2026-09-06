export type Locale = "en" | "fr";

export const copy = {
  en: {
    nav: {
      product: "Product",
      how: "How it works",
      gems: "Gems",
      privacy: "Privacy",
      download: "Download",
    },
    hero: {
      eyebrow: "AGENSTART FOR WINDOWS",
      title: "Your PC. Set up properly.",
      body: "AgenStart understands your PC, recommends the right apps, and installs only what you approve.",
      primary: "Download for Windows",
      secondary: "See how it works",
      cards: [
        ["Local analysis", "Your machine stays your machine."],
        ["39 trusted apps", "Curated, useful software."],
        ["Recommended", "Selected for the way you use this PC."],
        ["Installed", "Already there. No duplicate installs."],
      ],
    },
    portal: {
      eyebrow: "FROM MACHINE TO SETUP",
      title: "One PC. A setup that actually fits.",
    },
    story: {
      title: "A setup built around your machine.",
      steps: [
        {
          number: "01",
          label: "Analyse",
          title: "First, understand the machine.",
          body: "Windows, CPU, memory, graphics, storage, architecture and WinGet. No guessing. No generic setup list.",
        },
        {
          number: "02",
          label: "Profiles",
          title: "Then tell AgenStart what this PC is for.",
          body: "Work, development, study, creative work, gaming or everyday use. Choose more than one.",
        },
        {
          number: "03",
          label: "Recommendations",
          title: "Not more software. Better choices.",
          body: "AgenStart combines your machine, your uses and what is already installed.",
        },
        {
          number: "04",
          label: "Confirm",
          title: "Nothing happens without you.",
          body: "Choose exactly what you want, then confirm the final setup.",
        },
        {
          number: "05",
          label: "Install",
          title: "One approval. One clean setup.",
          body: "AgenStart installs through trusted Windows package sources and keeps the process visible.",
        },
      ],
    },
    manifesto: {
      first: "You don't need more apps.",
      second: "You need the right ones.",
      body: "AgenStart is not another download directory. It narrows the noise down to software that makes sense for this PC.",
    },
    gems: {
      eyebrow: "GEMS",
      title: "Meet the tools worth discovering.",
      body: "Some of the best Windows apps are not the ones everybody already knows. AgenStart calls them Gems.",
      apps: [
        ["LocalSend", "Send files between your devices without uploading them to a cloud service."],
        ["Everything", "Find files on Windows almost instantly."],
        ["WizTree", "See exactly what is eating your storage."],
        ["QuickLook", "Preview files with a single key."],
      ],
    },
    privacy: {
      eyebrow: "LOCAL-FIRST",
      title: "Your PC doesn't need to report to us.",
      body: "Machine analysis happens locally.",
      points: ["No personal files scanned", "No cloud hardware inventory required", "No account needed to understand your PC"],
      final: "Local means local.",
    },
    trust: {
      eyebrow: "TRUSTED INSTALLATION",
      title: "A short path from recommendation to install.",
      stages: ["Recommendation", "Trusted catalogue", "Official package identity", "WinGet / trusted source", "Your PC"],
      note: "No mystery download buttons. No random mirrors.",
    },
    beforeAfter: {
      before: "A new PC.",
      after: "Your PC. Ready for you.",
    },
    cta: {
      eyebrow: "READY WHEN YOU ARE",
      title: "Start with the PC you have.",
      body: "AgenStart will take it from there.",
      primary: "Download AgenStart",
      meta: "Windows 10 / 11 · x64",
      github: "View on GitHub",
    },
    footer: {
      line: "Designing the systems behind decisions.",
    },
  },
  fr: {
    nav: {
      product: "Produit",
      how: "Fonctionnement",
      gems: "Pépites",
      privacy: "Confidentialité",
      download: "Télécharger",
    },
    hero: {
      eyebrow: "AGENSTART POUR WINDOWS",
      title: "Votre PC. Configuré comme il faut.",
      body: "AgenStart comprend votre PC, recommande les bonnes applications et n'installe que ce que vous validez.",
      primary: "Télécharger pour Windows",
      secondary: "Voir comment ça fonctionne",
      cards: [
        ["Analyse locale", "Votre machine reste votre machine."],
        ["39 applications fiables", "Une sélection réellement utile."],
        ["Recommandé", "Adapté à votre façon d'utiliser ce PC."],
        ["Installé", "Déjà présent. Aucun doublon."],
      ],
    },
    portal: {
      eyebrow: "DE LA MACHINE À LA CONFIGURATION",
      title: "Un PC. Une configuration qui lui correspond vraiment.",
    },
    story: {
      title: "Une configuration construite autour de votre machine.",
      steps: [
        {
          number: "01",
          label: "Analyse",
          title: "D'abord, comprendre la machine.",
          body: "Windows, processeur, mémoire, carte graphique, stockage, architecture et WinGet. Pas d'approximation. Pas de liste générique.",
        },
        {
          number: "02",
          label: "Profils",
          title: "Ensuite, dites à AgenStart à quoi servira ce PC.",
          body: "Travail, développement, études, création, gaming ou usage quotidien. Vous pouvez en choisir plusieurs.",
        },
        {
          number: "03",
          label: "Recommandations",
          title: "Pas plus de logiciels. De meilleurs choix.",
          body: "AgenStart croise votre machine, vos usages et les logiciels déjà présents.",
        },
        {
          number: "04",
          label: "Confirm",
          title: "Rien ne se fait sans vous.",
          body: "Choisissez exactement ce que vous voulez, puis confirmez la configuration finale.",
        },
        {
          number: "05",
          label: "Installation",
          title: "Une validation. Une installation propre.",
          body: "AgenStart installe via des sources Windows fiables et garde le processus visible.",
        },
      ],
    },
    manifesto: {
      first: "Vous n'avez pas besoin de plus d'applications.",
      second: "Vous avez besoin des bonnes.",
      body: "AgenStart n'est pas un autre annuaire de téléchargement. Il réduit le bruit aux logiciels qui ont du sens pour ce PC.",
    },
    gems: {
      eyebrow: "PÉPITES",
      title: "Découvrez les outils qui méritent d'être connus.",
      body: "Certaines des meilleures applications Windows ne sont pas celles que tout le monde connaît déjà. AgenStart les appelle des Gems.",
      apps: [
        ["LocalSend", "Envoyez des fichiers entre vos appareils sans les charger sur un service cloud."],
        ["Everything", "Retrouvez vos fichiers Windows presque instantanément."],
        ["WizTree", "Voyez exactement ce qui consomme votre stockage."],
        ["QuickLook", "Prévisualisez un fichier avec une seule touche."],
      ],
    },
    privacy: {
      eyebrow: "LOCAL-FIRST",
      title: "Votre PC n'a pas besoin de nous rendre des comptes.",
      body: "L'analyse de la machine s'effectue localement.",
      points: ["Aucun fichier personnel analysé", "Aucun inventaire matériel cloud requis", "Aucun compte nécessaire pour comprendre votre PC"],
      final: "Local veut dire local.",
    },
    trust: {
      eyebrow: "INSTALLATION FIABLE",
      title: "Un chemin court entre la recommandation et l'installation.",
      stages: ["Recommandation", "Catalogue fiable", "Identité du package vérifiée", "WinGet / source fiable", "Votre PC"],
      note: "Pas de bouton de téléchargement douteux. Pas de miroir aléatoire.",
    },
    beforeAfter: {
      before: "Un PC neuf.",
      after: "Votre PC. Prêt pour vous.",
    },
    cta: {
      eyebrow: "QUAND VOUS VOULEZ",
      title: "Commencez avec le PC que vous avez.",
      body: "AgenStart s'occupe du reste.",
      primary: "Télécharger AgenStart",
      meta: "Windows 10 / 11 · x64",
      github: "Voir sur GitHub",
    },
    footer: {
      line: "Designing the systems behind decisions.",
    },
  },
} as const;
