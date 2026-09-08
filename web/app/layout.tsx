import type { Metadata } from "next";
import "./globals.css";
import "./landing-v2.css";

export const metadata: Metadata = {
  title: "AgenStart — Set up Windows properly",
  description:
    "AgenStart analyses your PC locally, recommends useful software and installs only what you approve.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" suppressHydrationWarning>
      <body>{children}</body>
    </html>
  );
}
