"use client";

import { useEffect } from "react";

export default function HomePage() {
  useEffect(() => {
    const saved = window.localStorage.getItem("agenstart-locale");
    const browser = window.navigator.language.toLowerCase().startsWith("fr") ? "fr" : "en";
    const locale = saved === "fr" || saved === "en" ? saved : browser;
    window.location.replace(`/${locale}`);
  }, []);

  return (
    <main className="route-loader" aria-live="polite">
      <span className="route-loader-mark">A</span>
      <p>Preparing AgenStart…</p>
    </main>
  );
}
