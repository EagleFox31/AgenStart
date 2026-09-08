import type { Metadata } from "next";
import "./globals.css";
import "./landing-v2.css";
import "./landing-v3.css";
import "./landing-v4.css";

const brandIcon =
  "https://raw.githubusercontent.com/EagleFox31/AgenStart/703df5cebee65fb03409037d16a36721401857dd/src/AgenStart.Desktop/Assets/agenstart-app-icon.png";

export const metadata: Metadata = {
  title: "AgenStart — Set up Windows properly",
  description:
    "AgenStart analyses your PC locally, recommends useful software and installs only what you approve.",
  icons: {
    icon: brandIcon,
    shortcut: brandIcon,
    apple: brandIcon,
  },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>{children}</body>
    </html>
  );
}
