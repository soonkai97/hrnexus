# Phase 7p: Remove the dark/light theme feature (one light theme)

> **Status:** `Implemented` (this PR)
> **Created:** 2026-11-14 | **Updated:** 2026-11-14
> **Depends on:** Phase 7n (one web app). Independent of everything else.
> **Why:** the owner asked (14 Nov 2026) to remove the dark/light theme feature and keep light. Every user already sees light by default (`ThemeService` fell back to `'light'`), so the toggle, the dark palette and the per-theme override block were extra to maintain, test and design for.

---

## Objectives

1. **One light theme.** No toggle button, no theme service, no stored preference, no dark palette, no PrimeNG dark mode.
2. **No visual change** for anyone who was on the default (light): the app looks pixel-identical before and after.
3. The next stylesheet author has one palette to think about, and the docs say so.

---

## Scope

### In scope

- Delete `ThemeService`, `ThemeToggleComponent` and their spec; remove the toggle from the shell top bar and the login and forgot-password pages (and the `.login-controls` styles that positioned it).
- `styles.scss`: the light colour values become the single `:root` palette; the old `[data-theme="light"]` block becomes a `:root` block (same specificity, same selectors that matched before), so every rule keeps winning exactly as it did; remove `color-scheme: dark`.
- PrimeNG: `darkModeSelector: false`.
- CLAUDE.md, `fe-designer` agent file and the PR review checklist stop mentioning dark mode.

### Out of scope (deferred)

- `nginx/maintenance.html`, a standalone static page with its own dark styling; it never used the theme service. Restyle it if it should match the app.
- Hard-coded dark-looking values inside individual component styles (for example the translucent glass panel behind the login card). They already render as part of today's light look; changing them would change the look this phase promises to keep.
- A leftover `nc-theme` entry in users' browser storage is harmless and is ignored.

---

## Verification

- **Pixel comparison:** before the change, light-theme screenshots of the login, forgot-password, dashboard, staff, leave, roles, policy, payroll locks, cash advances, profile and my-leave pages (and the login page at 360 px) were captured against the running stack; after the change and a no-cache rebuild, the same screenshots are compared with Playwright (`maxDiffPixelRatio` 0.002). The throwaway comparison spec is not committed. The only expected differences are the removed toggle button on the top bar and the login pages (those areas are masked or acknowledged).
- Unit tests, ESLint, Prettier ratchet, production build, full E2E.

---

## Rollback

Revert the squash-merge. No data or API changes; the browser's stored `nc-theme` value is not read by anything.

---

## Sign-off

- [x] The toggle, service and dark palette are gone; light is unchanged
- [x] CLAUDE.md and the designer checklist describe one theme
