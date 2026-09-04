# AgenStart UI reference

Status: approved visual direction for the desktop MVP

This document is the implementation reference for the Avalonia desktop shell and the guided setup flow.

## Canonical mockups

Full-resolution source mockups are stored in Google Drive:

- Folder: https://drive.google.com/drive/folders/1AxFw_TTsezc6pU8bYEf9je5qfSRctgTR
- 01 — Overview: https://drive.google.com/file/d/1vIyHpAWnVk4SOgxGh70Yb9TO8x51PmV3/view
- 02 — Your PC: https://drive.google.com/file/d/1F8KPtcVFHIzZDEdyJMM-F9cec_UA9C41/view
- 03 — Usage profile: https://drive.google.com/file/d/1nSwKZyBfhVRMFEFYJ7RGpURB02TEBhjM/view
- 04 — Recommendations: https://drive.google.com/file/d/1UQdWbEkkGkOYg_5lfTKz8m1D_b60GojS/view
- 05 — Review: https://drive.google.com/file/d/1XloY8YAXAx7EUVPrBLgHG3H-2fdMG1ap/view
- 06 — Installation: https://drive.google.com/file/d/1lu89O5-TPFk71lKlLe0hSi8jgMZf_Rqg/view
- 07 — Report: https://drive.google.com/file/d/1AslAwxRg2GJZMlgjheoTCNP0HhXmyKsw/view
- 08 — History: https://drive.google.com/file/d/1L0MLtXKgTAqvxg3SjQhzLk_Dk9UNubcN/view
- 09 — Settings: https://drive.google.com/file/d/1cn7vYYNPVB7xSl7pUiAqSZ4WipB39xdN/view

## Visual system

- compact dark navy navigation
- warm off-white content surface
- muted teal as the only primary accent
- thin dividers and restrained outlines
- minimal shadows
- restrained corner radius
- strong typography hierarchy
- generous whitespace
- desktop-first proportions
- Material 3 restraint + Linear-like precision + Windows 11 familiarity

Target simplification: 8/10.
Target visual density: 4/10.

The UI must not drift into SaaS dashboard, app-store, cyberpunk, gaming, glassmorphism or oversized marketing-page patterns.

## AgenStart identity

Production direction: **Concept 03** from the approved Windows smoke-test logo explorations.

The identity combines:

- the dark navy AgenStart `A`
- a teal forward-progress / automation route
- the Windows / PC block motif
- the `AgenStart` wordmark
- `BY AGENSTUDIO` as the product-signature line when the full lockup is used

Desktop source assets:

- `src/AgenStart.Desktop/Assets/agenstart-app-icon.png` — runtime/window/sidebar mark
- `src/AgenStart.Desktop/Assets/agenstart-app-icon.ico` — Windows executable icon with 16, 24, 32, 48 and 64 px frames

Usage rules:

- **Dark navigation:** use the light app-icon tile alongside a white `AgenStart` wordmark and teal `BY AGENSTUDIO` signature.
- **Light surfaces:** use the navy/teal mark on the warm off-white background; avoid placing the full lockup inside decorative cards unless branding is the content.
- **Small sizes:** use the app-icon mark only. Never shrink the complete wordmark into the Windows title bar/taskbar.
- **Clear space:** preserve at least one quarter of the icon width around the standalone mark and at least the cap-height of `AgenStart` around the full lockup.
- **Minimum practical sizes:** 16 px is icon-only; 24/32/48 px remain icon-only; the full lockup begins at roughly 120 px of usable horizontal width.
- **Do not recolor status semantics from the logo palette.** Teal is the product accent, while success/warning/error retain their own semantic colors.

The Windows executable, window chrome and taskbar must resolve to the same AgenStart mark. Generic Avalonia/.NET icons are not acceptable in production builds.

## Navigation model

Primary guided flow:

Overview → Your PC → Usage profile → Recommendations → Review → Installation → Report

Secondary navigation:

History · Settings

Future workflow steps should remain visually muted until their prerequisites are satisfied rather than behaving like unrelated navigation destinations.

## Semantic corrections to apply in implementation

The mockups are visual references, not literal data contracts. The following corrections are intentional:

### Your PC

Do not label raw hardware as `Compatible` without a target requirement. Prefer `Detected`, `Available`, `Supported`, `Unknown` or `Attention`. Compatibility belongs to a specific application or requirement.

### Review

The summary counts must reconcile exactly with the list. An optional unselected application uses an `Add` action, not `Remove`.

Starting the installation is the explicit approval boundary. Do not add a redundant blanket consent checkbox that conflicts with package-agreement handling.

### Installation

A package cannot simultaneously be actively installing and failed. Failure state stays attached to the affected queue item. The right-hand detail pane represents only the current operation.

### Report

AgenStart version and WinGet version are distinct. MVP examples should use `0.1.0-alpha` for AgenStart where a product version is shown.

Provider presentation should normalize to `WinGet`, with trusted sources such as `winget` and `msstore` shown separately when useful.

### History

`Available to export` is not the same as `Already exported`. Keep those states distinct.

## Product UX rules

Every screen should make these questions easy to answer:

1. What does AgenStart know?
2. Why is this being recommended?
3. What will happen if I continue?
4. What has already happened?
5. What still requires my approval?

The user remains the final authority before any machine-changing operation.

## Implementation intent

Use these mockups as the design reference for Issue #8. Build reusable Avalonia primitives first: shell, navigation, typography, spacing, status semantics, list rows, action buttons, progress states and detail panes. Then compose the individual screens from those primitives rather than reproducing each mockup as isolated XAML.
