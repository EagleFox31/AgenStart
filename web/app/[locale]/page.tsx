import { notFound } from "next/navigation";
import { LandingExperience } from "@/components/LandingExperience";
import type { Locale } from "@/lib/copy";

export function generateStaticParams() {
  return [{ locale: "en" }, { locale: "fr" }];
}

export default async function LocalePage({
  params,
}: {
  params: Promise<{ locale: string }>;
}) {
  const { locale } = await params;
  if (locale !== "en" && locale !== "fr") {
    notFound();
  }

  return <LandingExperience locale={locale as Locale} />;
}
