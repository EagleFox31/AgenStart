"use client";

import { useEffect, useRef, useState } from "react";
import gsap from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";
import Lenis from "lenis";
import { copy, type Locale } from "@/lib/copy";

const appRows = [
  ["VS Code", "Recommended", "status-blue"],
  ["PowerToys", "Installed", "status-green"],
  ["LocalSend", "Gem", "status-purple"],
  ["Docker Desktop", "Attention", "status-red"],
] as const;

function ProductMock({ activeStep }: { activeStep: number }) {
  const labels = ["Overview", "Your PC", "Usage profile", "Recommendations", "Confirm", "Installation", "Report"];
  const active = [1, 2, 3, 4, 5][Math.min(activeStep, 4)];

  return (
    <div className="product-screen">
      <aside className="product-sidebar">
        <div className="product-brand">AgenStart</div>
        <div className="product-brand-sub">BY AGENSTUDIO</div>
        <div className="product-nav">
          {labels.map((label, index) => (
            <div key={label} className={`product-nav-item ${index === active ? "is-active" : ""}`}>
              <span>{index === active ? "●" : "○"}</span>
              {label}
            </div>
          ))}
        </div>
      </aside>

      <div className="product-main">
        <div className="product-kicker">{activeStep === 0 ? "YOUR PC" : activeStep === 1 ? "USAGE PROFILE" : activeStep === 2 ? "RECOMMENDATIONS" : activeStep === 3 ? "CONFIRM" : "INSTALLATION"}</div>
        <h3>{activeStep === 0 ? "Your PC" : activeStep === 1 ? "What will you use this PC for?" : activeStep === 2 ? "Recommended for this PC" : activeStep === 3 ? "Confirm your setup" : "Installing your setup"}</h3>
        <p className="product-subtitle">AgenStart keeps the setup precise, visible and under your control.</p>

        {activeStep === 0 && (
          <div className="machine-grid">
            {["Windows 11 Pro", "Intel Core i7", "16 GB memory", "NVIDIA graphics", "476 GB free", "WinGet available"].map((item) => (
              <div className="machine-row" key={item}>
                <span>{item}</span><b>✓</b>
              </div>
            ))}
          </div>
        )}

        {activeStep === 1 && (
          <div className="profile-grid">
            {["Personal", "Business", "Study", "Development", "Creative", "Gaming"].map((item, index) => (
              <div className={`profile-card ${[1, 3, 4].includes(index) ? "is-selected" : ""}`} key={item}>
                <span className="profile-check">{[1, 3, 4].includes(index) ? "✓" : ""}</span>
                <strong>{item}</strong>
              </div>
            ))}
          </div>
        )}

        {activeStep === 2 && (
          <div className="app-list">
            {appRows.map(([name, status, color], index) => (
              <div className="app-row" key={name}>
                <div className="app-order">0{index + 1}</div>
                <div className="app-logo">{name.slice(0, 1)}</div>
                <div className="app-copy"><strong>{name}</strong><span>Useful software selected for this setup.</span></div>
                <div className={`app-status ${color}`}>{status}</div>
                <div className="app-check">{index < 3 ? "✓" : ""}</div>
              </div>
            ))}
          </div>
        )}

        {activeStep === 3 && (
          <div className="confirm-plan">
            <div><span>5</span><small>in plan</small></div>
            <div><span>1</span><small>already installed</small></div>
            <div><span>4</span><small>to install</small></div>
            <div className="confirm-list">
              {["VS Code", "LocalSend", "Bitwarden", "WizTree"].map((item) => <div key={item}><b>✓</b>{item}</div>)}
            </div>
            <button type="button" className="mock-primary">Confirm</button>
          </div>
        )}

        {activeStep >= 4 && (
          <div className="install-list">
            {["VS Code", "PowerToys", "LocalSend", "Bitwarden"].map((item, index) => (
              <div className="install-row" key={item}>
                <span>{item}</span>
                <div className="install-progress"><i style={{ width: index === 3 ? "68%" : "100%" }} /></div>
                <strong>{index === 3 ? "Installing" : "Installed ✓"}</strong>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export function LandingExperience({ locale }: { locale: Locale }) {
  const t = copy[locale];
  const root = useRef<HTMLDivElement>(null);
  const [activeStep, setActiveStep] = useState(0);

  useEffect(() => {
    document.documentElement.lang = locale;
    window.localStorage.setItem("agenstart-locale", locale);
    gsap.registerPlugin(ScrollTrigger);

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (reduced) return;

    const lenis = new Lenis({ duration: 1.05, smoothWheel: true });
    let raf = 0;
    const frame = (time: number) => {
      lenis.raf(time);
      raf = requestAnimationFrame(frame);
    };
    raf = requestAnimationFrame(frame);
    lenis.on("scroll", ScrollTrigger.update);

    const ctx = gsap.context(() => {
      gsap.from(".hero-reveal", {
        y: 54,
        opacity: 0,
        duration: 1.15,
        stagger: 0.09,
        ease: "power3.out",
      });

      gsap.from(".hero-float", {
        y: 34,
        opacity: 0,
        scale: 0.96,
        duration: 1,
        stagger: 0.12,
        delay: 0.35,
        ease: "power3.out",
      });

      gsap.to(".hero-bg", {
        yPercent: 18,
        scale: 1.1,
        ease: "none",
        scrollTrigger: { trigger: ".hero", start: "top top", end: "bottom top", scrub: true },
      });
      gsap.to(".hero-depth-front", {
        yPercent: -24,
        ease: "none",
        scrollTrigger: { trigger: ".hero", start: "top top", end: "bottom top", scrub: true },
      });

      gsap.fromTo(
        ".portal-shell",
        { scale: 0.7, borderRadius: 28 },
        {
          scale: 1,
          borderRadius: 8,
          ease: "none",
          scrollTrigger: {
            trigger: ".portal",
            start: "top top",
            end: "+=1200",
            pin: true,
            scrub: true,
          },
        },
      );

      gsap.utils.toArray<HTMLElement>(".story-step").forEach((step, index) => {
        ScrollTrigger.create({
          trigger: step,
          start: "top 58%",
          end: "bottom 42%",
          onEnter: () => setActiveStep(index),
          onEnterBack: () => setActiveStep(index),
        });
      });

      gsap.to(".manifesto-cloud", {
        xPercent: -18,
        ease: "none",
        scrollTrigger: { trigger: ".manifesto", start: "top bottom", end: "bottom top", scrub: true },
      });

      gsap.utils.toArray<HTMLElement>(".gem-card").forEach((card, index) => {
        gsap.to(card, {
          yPercent: index % 2 === 0 ? -24 : 18,
          rotate: index % 2 === 0 ? -1.5 : 1.5,
          ease: "none",
          scrollTrigger: { trigger: ".gems", start: "top bottom", end: "bottom top", scrub: 0.7 },
        });
      });

      gsap.utils.toArray<HTMLElement>(".trust-stage").forEach((stage) => {
        gsap.from(stage, {
          opacity: 0.25,
          y: 28,
          scrollTrigger: { trigger: stage, start: "top 78%", end: "top 50%", scrub: true },
        });
      });

      gsap.fromTo(
        ".ready-panel",
        { clipPath: "inset(0 100% 0 0)" },
        {
          clipPath: "inset(0 0% 0 0)",
          ease: "none",
          scrollTrigger: { trigger: ".before-after", start: "top top", end: "+=900", pin: true, scrub: true },
        },
      );
    }, root);

    return () => {
      ctx.revert();
      cancelAnimationFrame(raf);
      lenis.destroy();
    };
  }, [locale]);

  const otherLocale = locale === "en" ? "fr" : "en";

  return (
    <div ref={root} className="site-shell">
      <header className="site-nav">
        <a className="brand" href={`/${locale}`} aria-label="AgenStart home"><span>A</span><strong>AgenStart</strong></a>
        <nav className="nav-links" aria-label="Main navigation">
          <a href="#product">{t.nav.product}</a>
          <a href="#how">{t.nav.how}</a>
          <a href="#gems">{t.nav.gems}</a>
          <a href="#privacy">{t.nav.privacy}</a>
        </nav>
        <div className="nav-actions">
          <a className="lang-link" href={`/${otherLocale}`} onClick={() => window.localStorage.setItem("agenstart-locale", otherLocale)}>{otherLocale.toUpperCase()}</a>
          <a className="nav-download" href="#download">{t.nav.download}</a>
        </div>
      </header>

      <main>
        <section className="hero" id="product">
          <div className="hero-bg" aria-hidden="true" />
          <div className="hero-shade" aria-hidden="true" />
          <div className="hero-content">
            <div className="hero-copy">
              <p className="eyebrow hero-reveal">{t.hero.eyebrow}</p>
              <h1 className="hero-reveal">{t.hero.title}</h1>
              <p className="hero-body hero-reveal">{t.hero.body}</p>
              <div className="hero-actions hero-reveal">
                <a className="button button-primary" href="#download">{t.hero.primary}</a>
                <a className="button button-ghost" href="#how">{t.hero.secondary} <span>↓</span></a>
              </div>
            </div>

            <div className="hero-depth-front" aria-label="AgenStart highlights">
              {t.hero.cards.map(([title, body], index) => (
                <article className={`hero-float float-${index + 1}`} key={title}>
                  <div className="float-icon">{index === 0 ? "⌂" : index === 1 ? "39" : index === 2 ? "✦" : "✓"}</div>
                  <div><strong>{title}</strong><p>{body}</p></div>
                </article>
              ))}
            </div>

            <div className="hero-device" aria-hidden="true">
              <div className="device-frame"><ProductMock activeStep={2} /></div>
            </div>
          </div>
          <div className="hero-scroll-mark">SCROLL <span>↓</span></div>
        </section>

        <section className="portal" id="how">
          <div className="portal-intro">
            <p className="eyebrow">{t.portal.eyebrow}</p>
            <h2>{t.portal.title}</h2>
          </div>
          <div className="portal-shell"><ProductMock activeStep={0} /></div>
        </section>

        <section className="story-section">
          <div className="story-sticky">
            <p className="eyebrow">PRODUCT STORY</p>
            <h2>{t.story.title}</h2>
            <div className="story-product"><ProductMock activeStep={activeStep} /></div>
          </div>
          <div className="story-steps">
            {t.story.steps.map((step) => (
              <article className="story-step" key={step.number}>
                <div className="story-number">{step.number}</div>
                <p className="story-label">{step.label}</p>
                <h3>{step.title}</h3>
                <p>{step.body}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="manifesto">
          <div className="manifesto-copy">
            <h2>{t.manifesto.first}</h2>
            <h2 className="manifesto-accent">{t.manifesto.second}</h2>
            <p>{t.manifesto.body}</p>
          </div>
          <div className="manifesto-cloud" aria-hidden="true">
            {["PowerToys", "LocalSend", "Everything", "Bitwarden", "VS Code", "Obsidian", "WizTree", "QuickLook"].map((name, index) => (
              <span style={{ transform: `translateY(${(index % 3) * 34}px)` }} key={name}>{name}</span>
            ))}
          </div>
        </section>

        <section className="gems" id="gems">
          <div className="section-heading">
            <p className="eyebrow">{t.gems.eyebrow}</p>
            <h2>{t.gems.title}</h2>
            <p>{t.gems.body}</p>
          </div>
          <div className="gem-grid">
            {t.gems.apps.map(([name, body], index) => (
              <article className={`gem-card gem-${index + 1}`} key={name}>
                <div className="gem-mark">◆</div>
                <div><strong>{name}</strong><p>{body}</p></div>
              </article>
            ))}
          </div>
        </section>

        <section className="privacy" id="privacy">
          <div className="privacy-bg" aria-hidden="true" />
          <div className="privacy-shade" aria-hidden="true" />
          <div className="privacy-content">
            <p className="eyebrow">{t.privacy.eyebrow}</p>
            <h2>{t.privacy.title}</h2>
            <p className="privacy-lead">{t.privacy.body}</p>
            <div className="privacy-points">
              {t.privacy.points.map((point, index) => <div key={point}><span>0{index + 1}</span><strong>{point}</strong></div>)}
            </div>
            <p className="privacy-final">{t.privacy.final}</p>
          </div>
        </section>

        <section className="trust">
          <div className="section-heading compact">
            <p className="eyebrow">{t.trust.eyebrow}</p>
            <h2>{t.trust.title}</h2>
          </div>
          <div className="trust-flow">
            {t.trust.stages.map((stage, index) => (
              <div className="trust-stage" key={stage}>
                <span>0{index + 1}</span>
                <strong>{stage}</strong>
                {index < t.trust.stages.length - 1 && <i>↓</i>}
              </div>
            ))}
          </div>
          <p className="trust-note">{t.trust.note}</p>
        </section>

        <section className="before-after">
          <div className="before-panel"><div><span>01</span><h2>{t.beforeAfter.before}</h2><p>Windows. Clean slate. Nothing configured yet.</p></div></div>
          <div className="ready-panel"><div><span>02</span><h2>{t.beforeAfter.after}</h2><p>Useful apps, trusted sources, one setup you approved.</p></div><div className="ready-apps">{["VS Code", "PowerToys", "LocalSend", "Bitwarden", "Obsidian", "WizTree"].map((app) => <span key={app}>{app}</span>)}</div></div>
        </section>

        <section className="final-cta" id="download">
          <div className="cta-bg" aria-hidden="true" />
          <div className="cta-shade" aria-hidden="true" />
          <div className="cta-content">
            <p className="eyebrow">{t.cta.eyebrow}</p>
            <h2>{t.cta.title}</h2>
            <p>{t.cta.body}</p>
            <div className="cta-actions">
              <a className="button button-primary" href="https://github.com/EagleFox31/AgenStart">{t.cta.primary}</a>
              <span>{t.cta.meta}</span>
            </div>
            <a className="github-link" href="https://github.com/EagleFox31/AgenStart">{t.cta.github} ↗</a>
          </div>
        </section>
      </main>

      <footer>
        <div className="footer-brand"><span>A</span><div><strong>AgenStart</strong><small>by AgenStudio</small></div></div>
        <p>{t.footer.line}</p>
        <div className="footer-links"><a href="#product">Product</a><a href="https://github.com/EagleFox31/AgenStart">GitHub</a><a href="#privacy">Privacy</a><a href="#download">Download</a><a href={`/${otherLocale}`}>{otherLocale.toUpperCase()}</a></div>
      </footer>
    </div>
  );
}
