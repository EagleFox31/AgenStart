import type { Metadata } from "next";
import "./globals.css";
import "./landing-v2.css";
import "./landing-v3.css";
import "./landing-v4.css";
import "./landing-v5.css";

export const metadata: Metadata = {
  title: "AgenStart — Set up Windows properly",
  description:
    "AgenStart analyses your PC locally, recommends useful software and installs only what you approve.",
  icons: {
    icon: "/brand/agenstart-mark.svg",
    shortcut: "/brand/agenstart-mark.svg",
    apple: "/brand/agenstart-mark.svg",
  },
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>{children}</body>
    </html>
  );
}
