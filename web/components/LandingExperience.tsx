'use client';

import { useEffect, useRef, useState } from 'react';
import gsap from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import Lenis from 'lenis';

import { copy, type Locale } from '@/lib/copy';

gsap.registerPlugin(ScrollTrigger);

const ASSET_COMMIT = '703df5cebee65fb03409037d16a36721401857dd';
const repoAssetRoot = `https://raw.githubusercontent.com/EagleFox31/AgenStart/${ASSET_COMMIT}`;
const appLogoRoot = `${repoAssetRoot}/src/AgenStart.Desktop/Assets/AppLogos`;
const brandIcon = `${repoAssetRoot}/src/AgenStart.Desktop/Assets/agenstart-app-icon.png`;

const walkthroughScreens = ['analysis', 'profiles', 'recommendations', 'confirm', 'installation'] as const;
type WalkthroughScreen = (typeof walkthroughScreens)[number];

type AppRow = {
  order: string;
  logo: string;
  name: string;
  description: string;
  status: string;
  statusClass: string;
};

const appRows: AppRow[] = [
  { order: '01', logo: 'visual-studio-code.svg', name: 'VS Code', description: 'Code, debug and work with Git without carrying a full IDE.', status: 'Recommended', statusClass: 'status-blue' },
  { order: '02', logo: 'microsoft-powertoys.svg', name: 'PowerToys', description: 'Useful Windows tools for layouts, renaming, OCR and shortcuts.', status: 'Installed', statusClass: 'status-green' },
  { order: '03', logo: 'localsend.svg', name: 'LocalSend', description: 'Send files between nearby devices over your local network.', status: 'Gem', statusClass: 'status-purple' },
  { order: '04', logo: 'bitwarden.svg', name: 'Bitwarden', description: 'Keep passwords in one secure vault across your devices.', status: 'Recommended', statusClass: 'status-blue' },
  { order: '05', logo: 'wiztree.svg', name: 'WizTree', description: 'See what is actually using your storage in seconds.', status: 'Attention', statusClass: 'status-red' },
];

const confirmApps = [
  ['microsoft-powertoys.svg', 'PowerToys'],
  ['firefox-browser.svg', 'Firefox'],
  ['powershell.svg', 'PowerShell 7'],
  ['localsend.svg', 'LocalSend'],
] as const;

const installApps = [
  ['visual-studio-code.svg', 'VS Code', 100],
  ['microsoft-powertoys.svg', 'PowerToys', 100],
  ['localsend.svg', 'LocalSend', 78],
  ['bitwarden.svg', 'Bitwarden', 38],
] as const;

const manifestoApps = [
  ['microsoft-powertoys.svg', 'PowerToys'],
  ['localsend.svg', 'LocalSend'],
  ['everything.svg', 'Everything'],
  ['bitwarden.svg', 'Bitwarden'],
  ['obsidian.svg', 'Obsidian'],
  ['wiztree.svg', 'WizTree'],
] as const;

const gemLogoByName: Record<string, string> = {
  LocalSend: 'localsend.svg',
  Everything: 'everything.svg',
  WizTree: 'wiztree.svg',
  QuickLook: 'quicklook.svg',
};

type FeatureIconKind = 'files' | 'cloud' | 'account' | 'recommend' | 'catalogue' | 'identity' | 'winget' | 'pc';

function FeatureIcon({ kind }: { kind: FeatureIconKind }) {
  if (kind === 'files') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4.5 4.5h9l4 4v11h-13z"/><path d="M13.5 4.5v4h4"/><path d="M7.5 15.5l9-9"/><path d="M8 8l8 8"/></svg>;
  }
  if (kind === 'cloud') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 18h10a4 4 0 0 0 .5-7.97A6 6 0 0 0 6.3 8.7 4.5 4.5 0 0 0 7 18Z"/><path d="M5 5l14 14"/></svg>;
  }
  if (kind === 'account') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="3"/><path d="M5.5 19c1.3-3.3 3.5-5 6.5-5s5.2 1.7 6.5 5"/><path d="M5 5l14 14"/></svg>;
  }
  if (kind === 'recommend') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m12 3 1.7 4.3L18 9l-4.3 1.7L12 15l-1.7-4.3L6 9l4.3-1.7Z"/><path d="M5 17h14"/><path d="M8 21h8"/></svg>;
  }
  if (kind === 'catalogue') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H11v17H6.5A2.5 2.5 0 0 0 4 22Z"/><path d="M20 5.5A2.5 2.5 0 0 0 17.5 3H13v17h4.5A2.5 2.5 0 0 1 20 22Z"/></svg>;
  }
  if (kind === 'identity') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 19 6v5c0 4.7-2.9 8-7 10-4.1-2-7-5.3-7-10V6Z"/><path d="m8.5 12 2.2 2.2 4.8-4.8"/></svg>;
  }
  if (kind === 'winget') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="m7 9 3 3-3 3"/><path d="M12 15h5"/></svg>;
  }
  return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8"/><path d="M12 17v4"/><path d="m8 10 2 2 5-5"/></svg>;
}

function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`brand-lockup ${compact ? 'is-compact' : ''}`}>
      <img className="brand-logo" src={brandIcon} alt="" />
      <span className="brand-copy"><strong>AgenStart</strong>{!compact && <small>by AgenStudio</small>}</span>
    </span>
  );
}

function AppLogo({ file, name, className = '' }: { file: string; name: string; className?: string }) {
  return <img className={className} src={`${appLogoRoot}/${file}`} alt={`${name} logo`} loading="lazy" />;
}

function ProductMock({ screen }: { screen: WalkthroughScreen }) {
  return (
    <div className="product-screen" data-screen={screen}>
      <aside className="product-sidebar">
        <div className="product-brand-lockup"><img src={brandIcon} alt="" /><span><strong>AgenStart</strong><small>BY AGENSTUDIO</small></span></div>
        <div className="product-nav">
          {['Overview', 'Your PC', 'Usage profile', 'Recommendations', 'Confirm', 'Installation'].map((item, index) => {
            const activeIndex = screen === 'analysis' ? 1 : screen === 'profiles' ? 2 : screen === 'recommendations' ? 3 : screen === 'confirm' ? 4 : 5;
            return <div className={`product-nav-item ${index === activeIndex ? 'is-active' : ''}`} key={item}><span>{index === activeIndex ? '●' : '○'}</span>{item}</div>;
          })}
        </div>
      </aside>

      <div className="product-main">
        {screen === 'analysis' && <><div className="product-kicker">YOUR PC</div><h3>Your PC</h3><p className="product-subtitle">AgenStart detected the essentials locally.</p><div className="machine-grid">{[['Windows 11 Pro','Detected'],['Intel Core i7','Detected'],['16 GB memory','Available'],['NVIDIA graphics','Detected'],['64-bit','Supported'],['WinGet','Available']].map(([value,state]) => <div className="machine-row" key={value}><span>{value}</span><b>✓ {state}</b></div>)}</div></>}

        {screen === 'profiles' && <><div className="product-kicker">USAGE PROFILE</div><h3>What will you use this PC for?</h3><p className="product-subtitle">Choose one or more. Your uses are additive.</p><div className="profile-grid">{['Personal / Everyday','Business / Work','Study / Learning','Development','Creative','Gaming'].map((profile,i) => <div className={`profile-card ${[0,3,4].includes(i) ? 'is-selected' : ''}`} key={profile}>{[0,3,4].includes(i) && <span className="profile-check">✓</span>}{profile}</div>)}</div></>}

        {screen === 'recommendations' && <><div className="product-kicker">RECOMMENDATIONS</div><h3>Recommended for this PC</h3><p className="product-subtitle">A curated list based on this machine, your uses and what is already installed.</p><div className="app-list">{appRows.map(app => <div className="app-row" key={app.name}><span className="app-order">{app.order}</span><span className="app-logo"><AppLogo file={app.logo} name={app.name} /></span><span className="app-copy"><strong>{app.name}</strong><span>{app.description}</span></span><span className={`app-status ${app.statusClass}`}>{app.status}</span><span className="app-check">✓</span></div>)}</div></>}

        {screen === 'confirm' && <><div className="product-kicker">CONFIRM</div><h3>Confirm your setup</h3><p className="product-subtitle">Nothing is installed until you approve this plan.</p><div className="confirm-plan"><div><span>8</span><small>in plan</small></div><div><span>2</span><small>already installed</small></div><div><span>6</span><small>to install</small></div><div className="confirm-list">{confirmApps.map(([file,name]) => <div key={name}><AppLogo file={file} name={name} className="confirm-app-icon"/><span>{name}</span><b>✓</b></div>)}</div><button className="mock-primary">Confirm</button></div></>}

        {screen === 'installation' && <><div className="product-kicker">INSTALLATION</div><h3>Setting up this PC</h3><p className="product-subtitle">Trusted package sources. One calm queue.</p><div className="install-list">{installApps.map(([file,name,progress]) => <div className="install-row" key={name}><span className="install-app"><AppLogo file={file} name={name} className="install-app-icon"/>{name}</span><div className="install-progress"><i style={{ width: `${progress}%` }} /></div><strong>{progress === 100 ? 'Verified' : 'Installing'}</strong></div>)}</div></>}
      </div>
    </div>
  );
}

export function LandingExperience({ locale }: { locale: Locale }) {
  const rootRef = useRef<HTMLDivElement>(null);
  const [activeScreen, setActiveScreen] = useState<WalkthroughScreen>('analysis');
  const t = copy[locale];
  const otherLocale: Locale = locale === 'en' ? 'fr' : 'en';

  useEffect(() => {
    const root = rootRef.current;
    if (!root) return;
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduceMotion) return;

    const lenis = new Lenis({ duration: 1.05, smoothWheel: true });
    let rafId = 0;
    const raf = (time: number) => { lenis.raf(time); rafId = requestAnimationFrame(raf); };
    rafId = requestAnimationFrame(raf);
    lenis.on('scroll', ScrollTrigger.update);

    const context = gsap.context(() => {
      gsap.fromTo('.hero-bg', { scale: 1.045 }, { scale: 1.12, yPercent: 9, ease: 'none', scrollTrigger: { trigger: '.hero', start: 'top top', end: 'bottom top', scrub: true } });
      gsap.from('.hero-copy > *', { y: 38, opacity: 0, duration: 1.05, stagger: 0.09, ease: 'power3.out' });
      gsap.from('.hero-float', { y: 28, opacity: 0, scale: 0.96, duration: 1, stagger: 0.12, ease: 'power3.out', delay: 0.3 });
      gsap.to('.float-1', { yPercent: -44, ease: 'none', scrollTrigger: { trigger: '.hero', scrub: true } });
      gsap.to('.float-2', { yPercent: -25, ease: 'none', scrollTrigger: { trigger: '.hero', scrub: true } });
      gsap.to('.float-3', { yPercent: -62, ease: 'none', scrollTrigger: { trigger: '.hero', scrub: true } });
      gsap.fromTo('.portal-shell', { scale: 0.72, borderRadius: 28 }, { scale: 1, borderRadius: 4, ease: 'none', scrollTrigger: { trigger: '.portal', start: 'top top', end: 'bottom bottom', scrub: true, pin: '.portal-shell' } });
      gsap.to('.gems-bg', { yPercent: 10, scale: 1.18, ease: 'none', scrollTrigger: { trigger: '.gems', start: 'top bottom', end: 'bottom top', scrub: true } });
      gsap.to('.gem-1', { yPercent: -32, ease: 'none', scrollTrigger: { trigger: '.gems', scrub: true } });
      gsap.to('.gem-2', { yPercent: 24, ease: 'none', scrollTrigger: { trigger: '.gems', scrub: true } });
      gsap.to('.gem-3', { yPercent: -48, ease: 'none', scrollTrigger: { trigger: '.gems', scrub: true } });
      gsap.to('.gem-4', { yPercent: 36, ease: 'none', scrollTrigger: { trigger: '.gems', scrub: true } });
      gsap.to('.privacy-bg', { yPercent: 9, scale: 1.12, ease: 'none', scrollTrigger: { trigger: '.privacy', start: 'top bottom', end: 'bottom top', scrub: true } });
      gsap.to('.cta-bg', { yPercent: 8, scale: 1.12, ease: 'none', scrollTrigger: { trigger: '.final-cta', start: 'top bottom', end: 'bottom top', scrub: true } });
      gsap.fromTo('.ready-panel', { clipPath: 'inset(0 100% 0 0)' }, { clipPath: 'inset(0 0% 0 0)', ease: 'none', scrollTrigger: { trigger: '.before-after', start: 'top top', end: 'bottom bottom', scrub: true, pin: true } });
      root.querySelectorAll<HTMLElement>('.story-step').forEach((step, index) => ScrollTrigger.create({ trigger: step, start: 'top center', end: 'bottom center', onEnter: () => setActiveScreen(walkthroughScreens[index] ?? 'analysis'), onEnterBack: () => setActiveScreen(walkthroughScreens[index] ?? 'analysis') }));
    }, root);

    return () => { cancelAnimationFrame(rafId); context.revert(); lenis.destroy(); };
  }, []);

  const privacyIcons: FeatureIconKind[] = ['files', 'cloud', 'account'];
  const trustIcons: FeatureIconKind[] = ['recommend', 'catalogue', 'identity', 'winget', 'pc'];

  return (
    <div ref={rootRef} className="site-shell">
      <nav className="site-nav">
        <a className="brand" href={`/${locale}`}><BrandLockup compact /></a>
        <div className="nav-links"><a href="#product">{t.nav.product}</a><a href="#how">{t.nav.how}</a><a href="#gems">{t.nav.gems}</a><a href="#privacy">{t.nav.privacy}</a></div>
        <div className="nav-actions"><a className="lang-link" href={`/${otherLocale}`}>{otherLocale.toUpperCase()}</a><a className="github-link" href="https://github.com/EagleFox31/AgenStart">GitHub</a><a className="nav-download" href="https://github.com/EagleFox31/AgenStart">{t.nav.download}</a></div>
      </nav>

      <section className="hero"><div className="hero-bg"/><div className="hero-shade"/><div className="hero-content"><div className="hero-copy"><p className="eyebrow">{t.hero.eyebrow}</p><h1>{t.hero.title}</h1><p className="hero-body">{t.hero.body}</p><div className="hero-actions"><a className="button button-primary" href="https://github.com/EagleFox31/AgenStart">{t.hero.primary}</a><a className="button button-ghost" href="#how">{t.hero.secondary} ↓</a></div></div></div><div className="hero-depth-front">{t.hero.cards.map(([title,body],index) => <div className={`hero-float float-${index+1}`} key={title}><span className="float-icon">{['⌁','39','✦','✓'][index]}</span><div><strong>{title}</strong><p>{body}</p></div></div>)}</div></section>

      <section id="product" className="portal"><div className="portal-intro"><p className="eyebrow">{t.portal.eyebrow}</p><h2>{t.portal.title}</h2></div><div className="portal-shell"><ProductMock screen="recommendations" /></div></section>

      <section id="how" className="story-section"><div className="story-sticky"><p className="eyebrow">02 · {t.nav.how}</p><h2>{t.story.title}</h2><div className="story-product"><ProductMock screen={activeScreen} /></div></div><div className="story-steps">{t.story.steps.map(step => <article className="story-step" key={step.title}><span className="story-number">{step.number}</span><p className="story-label">{step.label}</p><h3>{step.title}</h3><p>{step.body}</p></article>)}</div></section>

      <section className="manifesto"><div className="manifesto-copy"><p className="eyebrow">03 · CURATION</p><h2>{t.manifesto.first}<br/><span className="manifesto-accent">{t.manifesto.second}</span></h2><p>{t.manifesto.body}</p></div><div className="manifesto-cloud">{manifestoApps.map(([file,name]) => <span key={name}><AppLogo file={file} name={name} className="manifesto-app-logo"/>{name}</span>)}</div></section>

      <section id="gems" className="gems"><div className="gems-bg"/><div className="section-heading"><p className="eyebrow">{t.gems.eyebrow}</p><h2>{t.gems.title}</h2><p>{t.gems.body}</p></div><div className="gem-grid">{t.gems.apps.map(([name,body],index) => <article className={`gem-card gem-${index+1}`} key={name}><span className="gem-mark"><AppLogo file={gemLogoByName[name] ?? 'localsend.svg'} name={name} /></span><div><strong>{name}</strong><p>{body}</p></div></article>)}</div></section>

      <section id="privacy" className="privacy"><div className="privacy-bg"/><div className="privacy-shade"/><div className="privacy-content"><p className="eyebrow">{t.privacy.eyebrow}</p><h2>{t.privacy.title}</h2><p className="privacy-lead">{t.privacy.body}</p><div className="privacy-points">{t.privacy.points.map((point,index) => <div key={point}><span className="section-icon"><FeatureIcon kind={privacyIcons[index]} /></span><span className="privacy-number">0{index+1}</span><strong>{point}</strong></div>)}</div><p className="privacy-final">{t.privacy.final}</p></div></section>

      <section className="trust"><div className="section-heading compact"><p className="eyebrow">{t.trust.eyebrow}</p><h2>{t.trust.title}</h2></div><div className="trust-flow">{t.trust.stages.map((stage,index) => <div key={stage} style={{ display: 'contents' }}><div className="trust-stage"><span className="section-icon trust-icon"><FeatureIcon kind={trustIcons[index]} /></span><span className="trust-number">0{index+1}</span><strong>{stage}</strong></div>{index < t.trust.stages.length-1 && <span className="trust-arrow">→</span>}</div>)}</div><p className="trust-note">{t.trust.note}</p></section>

      <section className="before-after"><div className="before-panel"><div><p className="before-label">BEFORE</p><h2>{t.beforeAfter.before}</h2></div></div><div className="ready-panel"><div><p className="before-label">AFTER AGENSTART</p><h2>{t.beforeAfter.after}</h2></div></div></section>

      <section className="final-cta"><div className="cta-bg"/><div className="cta-shade"/><div className="cta-copy"><p className="eyebrow">{t.cta.eyebrow}</p><h2>{t.cta.title}</h2><p>{t.cta.body}</p><div className="cta-actions"><a className="button button-primary" href="https://github.com/EagleFox31/AgenStart">{t.cta.primary}</a></div><p className="cta-meta">{t.cta.meta}</p><a className="github-link" href="https://github.com/EagleFox31/AgenStart">{t.cta.github} ↗</a></div></section>

      <footer className="site-footer"><div className="footer-top"><div className="footer-brand"><BrandLockup /></div><div className="footer-links"><a href="#product">{t.nav.product}</a><a href="https://github.com/EagleFox31/AgenStart">GitHub</a><a href="#privacy">{t.nav.privacy}</a><a href={`/${otherLocale}`}>{otherLocale === 'fr' ? 'Français' : 'English'}</a></div></div><div className="footer-bottom"><span>© AgenStudio</span><span>{t.footer.line}</span></div></footer>
    </div>
  );
}
