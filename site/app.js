const RELEASE_API = "https://api.github.com/repos/EagleFox31/AgenStart/releases/latest";
const RELEASES_FALLBACK = "https://github.com/EagleFox31/AgenStart/releases/latest";

const header = document.querySelector("[data-header]");
const releaseLinks = [...document.querySelectorAll("[data-download-link]")];
const releaseVersionLabels = [...document.querySelectorAll("[data-release-version]")];
const releaseNote = document.querySelector("[data-release-note]");
const isFrench = document.documentElement.lang === "fr";

const setHeaderState = () => {
  header?.classList.toggle("is-scrolled", window.scrollY > 18);
};

setHeaderState();
window.addEventListener("scroll", setHeaderState, { passive: true });

document.querySelector("[data-year]").textContent = new Date().getFullYear();

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

    const targetUrl = asset?.browser_download_url || release.html_url || RELEASES_FALLBACK;
    const version = release.tag_name || (isFrench ? "Dernière version stable" : "Latest stable");

    releaseLinks.forEach((link) => {
      link.href = targetUrl;
      link.setAttribute(
        "aria-label",
        isFrench
          ? `Télécharger AgenStart ${version} pour Windows`
          : `Download AgenStart ${version} for Windows`,
      );
    });
    releaseVersionLabels.forEach((label) => { label.textContent = version; });

    if (releaseNote) {
      const size = asset?.size ? `${Math.round(asset.size / 1024 / 1024)} MB` : null;
      releaseNote.textContent = ["Windows 10/11 x64", version, size].filter(Boolean).join(" · ");
    }
  } catch (error) {
    console.info("AgenStart release metadata unavailable; using the stable releases URL.", error);
    releaseLinks.forEach((link) => { link.href = RELEASES_FALLBACK; });
  }
}

resolveLatestRelease();
