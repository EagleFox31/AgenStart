import { copyFile, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const siteDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(siteDirectory, "..");
const desktopAssetsDirectory = path.join(
  repositoryRoot,
  "src",
  "AgenStart.Desktop",
  "Assets",
);
const sourceDirectory = path.join(desktopAssetsDirectory, "AppLogos");
const outputDirectory = path.join(siteDirectory, "assets", "generated-logos");
const brandOutputDirectory = path.join(siteDirectory, "assets", "generated-brand");
const profilesPath = path.join(repositoryRoot, "docs", "product", "profiles.json");
const landingPath = path.join(siteDirectory, "index.html");

const LOGO_FILES = [
  "visual-studio-code.svg",
  "git.svg",
  "docker.svg",
  "firefox-browser.svg",
  "microsoft-powertoys.svg",
  "windows-terminal.svg",
  "postgresql.svg",
  "nodejs.svg",
];

const PROFILE_ICONS = {
  home: '<path d="M5 14 16 5l11 9"/><path d="M8 12.5V25h16V12.5"/><path d="M13 25v-7h6v7"/>',
  briefcase: '<rect x="5" y="10" width="22" height="15" rx="2"/><path d="M11 10V8h10v2M5 16h22"/>',
  code: '<path d="M11 8 4 16l7 8M21 8l7 8-7 8M19 5l-6 22"/>',
  palette: '<path d="M7 24 23 8l3 3-16 16H7v-3Z"/><path d="m19 12 3 3M5 7h7M8.5 3.5v7M22 23h5M24.5 20.5v5"/>',
  "graduation-cap": '<path d="M4 13l12-5 12 5-12 5L4 13Z"/><path d="M8 16v6c4 3 12 3 16 0v-6M28 13v8"/>',
  laptop: '<path d="M7 9h18v12H7z"/><path d="M4 21h24l-2 3H6z"/>',
  gamepad: '<path d="M9 11h14c4 0 6 3 5 8l-1 5c-.5 2-3 2-4 0l-2-4H11l-2 4c-1 2-3.5 2-4 0l-1-5c-1-5 1-8 5-8Z"/><path d="M10 15v6M7 18h6M21 16h.01M24 19h.01"/>',
  clapperboard: '<path d="M6 12h20v13H6z"/><path d="M6 12 10 6h5l-4 6h5l4-6h5l-4 6"/>',
};

const ENGLISH_COUNTS = [
  "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve",
];
const FRENCH_COUNTS = [
  "Zéro", "Un", "Deux", "Trois", "Quatre", "Cinq", "Six", "Sept", "Huit", "Neuf", "Dix", "Onze", "Douze",
];

await Promise.all([
  rm(outputDirectory, { recursive: true, force: true }),
  rm(brandOutputDirectory, { recursive: true, force: true }),
]);
await Promise.all([
  mkdir(outputDirectory, { recursive: true }),
  mkdir(brandOutputDirectory, { recursive: true }),
]);

for (const fileName of LOGO_FILES) {
  await copyFile(
    path.join(sourceDirectory, fileName),
    path.join(outputDirectory, fileName),
  );
}

await Promise.all([
  copyFile(
    path.join(desktopAssetsDirectory, "agenstart-app-icon.png"),
    path.join(brandOutputDirectory, "favicon.png"),
  ),
  copyFile(
    path.join(desktopAssetsDirectory, "agenstart-app-icon.ico"),
    path.join(brandOutputDirectory, "favicon.ico"),
  ),
]);

await writeGeneratedProfileSection();

console.log(
  `Prepared ${LOGO_FILES.length} landing logo assets, canonical AgenStart favicon artwork, and the profile section from profiles.json.`,
);

async function writeGeneratedProfileSection() {
  const registry = JSON.parse(await readFile(profilesPath, "utf8"));
  const profiles = [...(registry.profiles ?? [])].sort((left, right) => left.order - right.order);
  const active = profiles.filter((profile) => profile.status === "active");
  const planned = profiles.filter((profile) => profile.status === "planned");

  if (active.length === 0) {
    throw new Error("profiles.json must declare at least one active profile.");
  }

  const landing = await readFile(landingPath, "utf8");
  const newline = landing.includes("\r\n") ? "\r\n" : "\n";
  let next = landing;

  next = replaceRegion(next, "profile-status", renderStatus(active.length), newline);
  next = replaceRegion(next, "current-profiles", active.map((profile) => renderActiveCard(profile)).join(`${newline}${newline}`), newline);
  next = replaceRegion(next, "upcoming-profiles", planned.map((profile) => renderPlannedCard(profile)).join(`${newline}${newline}`), newline);
  next = replaceRegion(next, "flow-profile-chips", active.map((profile) => renderFlowChip(profile)).join(newline), newline);

  if (next !== landing) {
    await writeFile(landingPath, next, "utf8");
  }
}

function replaceRegion(html, name, inner, newline) {
  const start = `<!-- generated:${name} -->`;
  const end = `<!-- /generated:${name} -->`;
  const startIndex = html.indexOf(start);
  const endIndex = html.indexOf(end);
  if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex) {
    throw new Error(`Missing generated region '${name}' in site/index.html.`);
  }

  const indentMatch = html.slice(0, startIndex).match(/(?:^|\n)([ \t]*)$/);
  const indent = indentMatch?.[1] ?? "";
  const indentedInner = inner
    .split(/\r?\n/)
    .map((line) => (line.length === 0 ? "" : `${indent}${line}`))
    .join(newline);

  return `${html.slice(0, startIndex + start.length)}${newline}${indentedInner}${newline}${indent}${html.slice(endIndex)}`;
}

function renderStatus(count) {
  const englishCount = ENGLISH_COUNTS[count] ?? String(count);
  const frenchCount = FRENCH_COUNTS[count] ?? String(count);
  const english = `${englishCount} profiles already shape real recommendations in AgenStart.`;
  const french = `${frenchCount} profils orientent déjà les recommandations réelles d’AgenStart.`;
  return `<span data-en="${escapeAttribute(english)}" data-fr="${escapeAttribute(french)}">${escapeHtml(english)}</span>`;
}

function renderFlowChip(profile) {
  const label = requireText(profile.label, profile.id, "label");
  const className = profile.id === "development" ? "profile-chip active" : "profile-chip";
  return `<div class="${className}" data-profile-id="${escapeAttribute(profile.id)}" data-en="${escapeAttribute(label.en)}" data-fr="${escapeAttribute(label.fr)}">${escapeHtml(label.en)}</div>`;
}

function renderActiveCard(profile) {
  const label = requireText(profile.label, profile.id, "label");
  const description = requireText(profile.description, profile.id, "description");
  const icon = PROFILE_ICONS[profile.icon] ?? PROFILE_ICONS.home;
  return [
    `<article class="profile-card reveal" data-profile-id="${escapeAttribute(profile.id)}">`,
    `  <div class="profile-card-top">`,
    `    <span class="profile-card-icon" aria-hidden="true">`,
    `      <svg viewBox="0 0 32 32" fill="none">${icon}</svg>`,
    `    </span>`,
    `    <span class="profile-badge">Available now</span>`,
    `  </div>`,
    `  <h3 data-en="${escapeAttribute(label.en)}" data-fr="${escapeAttribute(label.fr)}">${escapeHtml(label.en)}</h3>`,
    `  <p data-en="${escapeAttribute(description.en)}" data-fr="${escapeAttribute(description.fr)}">${escapeHtml(description.en)}</p>`,
    `</article>`,
  ].join("\n");
}

function renderPlannedCard(profile) {
  const label = requireText(profile.label, profile.id, "label");
  const description = requireText(profile.description, profile.id, "description");
  return [
    `<article class="profile-card profile-card-coming" data-profile-id="${escapeAttribute(profile.id)}">`,
    `  <span class="profile-badge coming">Coming soon</span>`,
    `  <h3 data-en="${escapeAttribute(label.en)}" data-fr="${escapeAttribute(label.fr)}">${escapeHtml(label.en)}</h3>`,
    `  <p data-en="${escapeAttribute(description.en)}" data-fr="${escapeAttribute(description.fr)}">${escapeHtml(description.en)}</p>`,
    `</article>`,
  ].join("\n");
}

function requireText(value, id, field) {
  const english = value?.en?.trim();
  const french = value?.fr?.trim();
  if (!english || !french) {
    throw new Error(`Profile '${id}' is missing ${field}.en or ${field}.fr.`);
  }

  return { en: english, fr: french };
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll('"', "&quot;");
}
