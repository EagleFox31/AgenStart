import { copyFile, mkdir, rm } from "node:fs/promises";
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

console.log(
  `Prepared ${LOGO_FILES.length} landing logo assets and canonical AgenStart favicon artwork.`,
);
