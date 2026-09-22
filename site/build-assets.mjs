import { copyFile, mkdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const siteDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(siteDirectory, "..");
const sourceDirectory = path.join(
  repositoryRoot,
  "src",
  "AgenStart.Desktop",
  "Assets",
  "AppLogos",
);
const outputDirectory = path.join(siteDirectory, "assets", "generated-logos");

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

await rm(outputDirectory, { recursive: true, force: true });
await mkdir(outputDirectory, { recursive: true });

for (const fileName of LOGO_FILES) {
  await copyFile(
    path.join(sourceDirectory, fileName),
    path.join(outputDirectory, fileName),
  );
}

console.log(`Prepared ${LOGO_FILES.length} landing logo assets from the canonical desktop artwork.`);
