# Phase 7k: Beautiful own UI kit (replace PrimeNG)

> **Status:** `Draft`
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phase 7j (Locations removal) and 7i (English only, touches every template). Design work (steps 1 to 4) may start during 7j and 7i because it touches no code.

PrimeNG 22 is published by PrimeUI and shows an "Invalid PrimeUI License" badge unless a license is configured; Angular 22 requires it. HRNexus stays on Angular 21 + PrimeNG 21 (MIT, security fixes only) as a **bridge** and builds its own `app-*` kit, following IMS (`docs/architecture/frontend.md` decision 14: no component library; Angular CDK for behaviour only, inside the UI library, never exposed). The user added a first-class requirement: **the UI/UX must be really beautiful**.

## Objectives

1. **Beauty is a deliverable, not a side effect.** A signed-off style tile and five redesigned flagship screens exist before any bulk migration (design quality section below).
2. Zero `primeng`, `primeicons` and `@primeuix` imports; PrimeNG 21 removed from `package.json` when the last legacy file migrates.
3. One token system (`--hr-*`, role-named, light and dark designed separately) replaces `$neon-*` (118 uses), `--nc-*` (150 uses) and the Aura preset.
4. Every component accessible by construction: WCAG 2.2 AA (contrast script), 44px targets, designed focus rings, full keyboard flows.
5. Nothing regresses: E2E 58/58, unit counts not lower, Lighthouse accessibility reported per page.

## Scope

### In scope
- Design direction, tokens, motion, icons, the `app-*` kit, migration of both apps and shared-lib, lint and stylelint rules, the screenshot review ritual.
### Out of scope (deferred)
- Nx workspace and tiers (7g): the kit lives in `projects/shared-lib/src/lib/ui` and moves to `@hrnexus/ui` when 7g lands. A command palette and density modes (IMS has them) are later options. Charts, rich-text editor, file viewer.

## Design quality (first-class)

**Direction.** Copy IMS's principles (`docs/design/2026-refresh-visual.md`): calm, clear, quiet motion, dark mode as its own palette, one accent, colour only where it means something, no glassmorphism or decoration. Adapt to HR: data-dense desktop admin, calm phone-first staff app, human empty states.

**Step 1, design brief and style tile (before code).** One page each for light and dark: font choice and type scale (hierarchy, tabular numerals for money and hours), 8px spacing grid and radius scale, elevation, colour roles (`canvas`, `surface`, `surface-raised`, `surface-sunken`, `border`, `border-strong`, `text`, `text-muted`, `accent`, `success`, `warning`, `danger`, `info`, each with `-soft` and `on-*`), density rules (admin tables compact, staff app roomy), iconography, an illustration and empty-state style, loading skeletons, error and success feedback, motion principles (IMS `2026-refresh-motion.md`: five tokens, transform and opacity only, reduced-motion safe).

**Roles used (existing agents).** `ui-ux-designer`: flows, wireframes, empty/loading/error states, mobile-first staff app. `fe-designer`: tokens, component look and feel, responsive rules, theming. `qa-novice-staff` and `qa-power-manager`: usability review of each flagship screen. `fe-architect`: component contracts; independent review before every merge (`docs/team/PR-REVIEW.md`).

**Owner sign-off gates.** G1: style tile (light and dark). G2: redesigned **login, dashboard, staff list, leave page and staff-app home**, built as real `app-*` screens behind the kit's first components. Bulk migration starts only after G1 and G2. G3: final review of all pages.

**Beauty checklist (every page, every slice).** Alignment to the grid; consistent spacing rhythm; clear typographic hierarchy with one primary action per screen; contrast and focus rings that look designed; consistent hover, pressed, disabled, loading, empty and error states; no clipped or overflowing text (long names, long unbroken values); polished micro-interactions within the motion tokens; perceived performance (skeletons after 300 ms, no layout shift).

**Screenshot review ritual.** A Node script with Playwright `channel: 'msedge'` (headless Chromium shell fails in this sandbox) captures every page at 360, 768 and 1280 px, light and dark, before and after; the owner reviews side by side against the checklist; findings become fixes in the same PR. Without browser tools in the assistant session, the owner reviews the images, the assistant reviews structure and numbers.

**Accessibility is beauty.** `npm run contrast` (port of IMS `web/scripts/check-contrast.mjs`) proves AA for every token pair in both themes; focus rings are a designed token (2px, offset); keyboard flows are specified per component and tested; reduced motion zeroes distances; status is never colour alone.

## Dependencies

| Dependency | Type | Status |
|---|---|---|
| 7j and 7i merged (templates stable, English text inline) | Code | Pending |
| PrimeNG 21 stays installed until the allowlist is empty | Code | Done |
| Owner availability for gates G1, G2, G3 | Decision | Pending |
| Icon set licence check (below) | External | Pending |

## Facts measured on master

PrimeNG: 27 modules in 48 files (21 are specs providing `MessageService`/`ConfirmationService`). Template tags: `p-button` 106, `p-card` 40, `p-skeleton` 37, `p-tag` 24, `p-datepicker` 13, `p-select` 11, `p-menu` 10, `p-dialog` 10, `p-message` 8, `p-inputnumber` 8, tabs 9, `p-paginator` 6, `p-multiselect` 6, `p-selectbutton` 5, `p-checkbox` 5, `p-table` 4 (+10 sort icons), 5 singles; directives `pInputText` 38, `pTooltip` 12. Styling: 119 `styleClass` uses, 151 `.p-*` selectors in 30 SCSS files (8 `:deep(.p-` overrides), `providePrimeNG` + Aura in both apps, 158 icon uses (47 distinct `pi pi-*`).

## Component mapping

| PrimeNG | `app-*` replacement | Built on | Diff | Uses |
|---|---|---|---|---|
| p-button, pInputText, p-card, p-tag, p-badge, p-progressbar, p-message, p-skeleton, checkbox, selectbutton (radio-group), password | `app-button`, `app-input`, `app-card`, `app-status-chip`, `app-banner`, `app-skeleton`, `app-checkbox`, `app-segmented` | hand-built, tokens | S | ~270 |
| primeicons | `app-icon` (24px paths, `aria-hidden`) | inline SVG | S | 158 |
| pTooltip | `appTooltip` | native popover | S | 12 |
| p-inputnumber | `app-number-input` | hand-built, locale-free | M | 8 |
| **p-dialog, p-confirmdialog** | `app-dialog` (dialog on desktop, bottom sheet below 600px), `ConfirmService` | native `<dialog>` (focus trap, Esc free) | **L** | 11 |
| **p-toast** | `app-toast` host + `ToastService` (aria-live, errors persist) | hand-built | M | 1 |
| **p-datepicker** | `app-date-field` (text `dd/MM/yyyy`, hidden native input + `showPicker()`) and `app-time-field` (24 h) | native + hand-built | **L** | 13 |
| **p-select, p-multiselect** | `app-select`, `app-multi-select` (combobox, typeahead, `aria-activedescendant`) | CDK listbox + overlay | **L** | 17 |
| **p-table, p-sort, p-paginator** | `app-table` (server sort), `app-pager` (live region), card list on phones | hand-built + native popover column menu | **L** | 20 |
| **p-menu** | `app-menu` | native popover + CDK menu keys | M | 10 |
| p-tabs | `app-tabs` (arrow keys, `aria-selected`) | hand-built | M | 9 |

Hard six (bold) dominate the effort. IMS ships the same set except `menu` and `multiselect`, which are added here.

## Tokens, icons, theming

- Tokens are CSS custom properties `--hr-<group>-<name>` emitted once (`_themes.scss`); SCSS functions `color()`, `space()`, `font-size()`, `border-radius()`, `shadow()`, `duration()`, `easing()` return `var(--hr-...)`. No hex, px, radius, shadow or duration in a component. Stylelint (IMS `.stylelintrc.json`: `color-no-hex`, no `!important`, transitions only on opacity/transform) enforces it.
- Old names stay as aliases (`$neon-*`, `--nc-*`) until the last file migrates. Light, dark and system themes; theme switch crossfades.
- Icons: copy the owner's IMS `icon.paths.ts`, add the missing of the 47 names from Lucide (ISC licence, header recorded in the file); `app-icon` sizes 16/20/24/40, non-scaling stroke. Check the licence text in step 4.

## Migration strategy

1. Build kit components with specs and an a11y check each (Jest-style behaviour specs in Vitest here), tokens first.
2. Facades: pages use `app-*` only; PrimeNG stays behind them until replaced.
3. A Node codemod swaps the simple tags (`p-button`, `p-card`, `p-tag`, `p-skeleton`, `pInputText`, `p-message`, icons) and rewrites `styleClass`; the hard six are migrated by hand, page by page.
4. ESLint `no-restricted-imports` bans `primeng*`, `@primeuix*`, `primeicons` in **new** code and lists legacy files that may still import them (a **shrinking allowlist**: the list only gets shorter; an entry for a migrated file fails the build, same pattern as IMS's architecture tests). Also ban `@angular/animations` and the `autofocus` attribute, as IMS does.
5. When the list is empty: remove PrimeNG, `providePrimeNG`, Aura, the 21 spec providers and the `.p-*` SCSS.

## Ordering

7j, then 7i (every template), design steps 1 to 4 in parallel with them, kit foundation, **G2**, hard six and migration, then 7g (move to `@hrnexus/ui` with tiers), phase 8 onward. From the end of the foundation step new modules (phases 8 to 12) use `app-*` only, enforced by the lint ban; phase 8 starts after G2.

## Effort (working days, honest ranges)

| Group | Days | Reasoning |
|---|---|---|
| **Design**: brief + style tile (G1), 5 flagship screens, states and motion, checklist, review rounds | 8 to 10 | Separate from build; the owner's sign-off time is extra |
| Foundation: tokens, lint/stylelint, contrast script, icon, 11 simple components, screenshot script | 5 | Mostly mechanical, IMS patterns |
| Hard six (dialog/confirm/toast, datepicker + time, select/multiselect, table/sort/pager, menu, tabs) | 14 to 18 | Native `<dialog>`, popover and CDK cut the cost; a11y specs per component |
| Migration of 48 files, 119 `styleClass`, 151 selectors, 21 spec providers, codemod | 7 to 9 | Codemod handles simple tags |
| Visual QA rounds, a11y fixes, E2E, review | 5 | Screenshot ritual, Lighthouse |
| **Total** | **39 to 47** (about 8 to 9 weeks), design included | Hard six and design dominate |

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Accessibility regressions in hand-built comboboxes and datepicker | Med | High | CDK listbox, per-component keyboard specs, contrast script, qa-edge-accessibility run |
| Date handling (time zone, 24 h, `dd/MM/yyyy`) | Med | Med | One `formatDate`, `Asia/Kuala_Lumpur`, typed-text field like IMS |
| Scope creep (command palette, density, charts) | High | Med | Out of scope list, gates, phase doc updates |
| Looks generic or inconsistent | Med | High | Style tile and flagship gates before bulk work, beauty checklist |
| Service-worker caches serve old CSS after the swap | Low | Med | Hash-named assets, update prompt, verify after deploy |
| Long migration blocks HRM phases | Med | Med | New modules use `app-*` from G2, PrimeNG allowlist shrinks per PR |

## Implementation steps

1. `[design]` Design brief and style tile (light, dark): G1. Size M.
2. `[design]` Flagship screens (login, dashboard, staff list, leave, staff-app home) as wireframes + real screens: G2. L.
3. `[TDD]` Tokens, themes, stylelint, contrast script, ESLint ban + allowlist, screenshot script. M.
4. `[TDD]` Icon component and set (licence recorded); simple components with specs. M.
5. `[TDD]` Dialog, confirm, toast; table, sort, pager; menu, tabs. L.
6. `[TDD]` Datepicker/time, select, multi-select. L.
7. Codemod and page-by-page migration in PRs under 1500 lines; screenshots per PR. L.
8. Remove PrimeNG; final G3 review; update CLAUDE.md. S.

## Validation criteria

- [ ] No `primeng`, `primeicons`, `@primeuix` import; package removed
- [ ] `npm run contrast` passes in light and dark; WCAG 2.2 AA keyboard specs per component
- [ ] Per-component specs; unit counts not lower than master; E2E 58/58 green
- [ ] Screenshots at 360, 768, 1280 in light and dark approved against the beauty checklist (G1, G2, G3)
- [ ] Lighthouse accessibility score reported for every page; no horizontal scroll at 320 px or 400% zoom
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

## Rollback

PrimeNG 21 stays installed until the last PR, so any PR reverts alone. After removal, restore the previous release tag.

## Open questions

1. **Brand accent and tone:** keep a purple identity, or IMS's calm blue (`accent` `#2447c4` / `#8fb3ff`)? Decide at G1.
2. **Typeface:** system stack (IMS) or one self-hosted web font? Affects hierarchy and load.
3. **Density and a command palette** (IMS has both): include in this phase or after?

## Review notes and sign-off

_Filled in after the phase._ Sign-off: all validation criteria met; G1, G2, G3 recorded; review of all 7 perspectives; phase 8 plan updated; CLAUDE.md updated (no PrimeNG, `app-*` kit rules).
