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
