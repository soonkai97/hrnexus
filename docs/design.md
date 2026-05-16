# UI Design — Nightclub Workforce

**Status:** as-is + gaps + target end-state
**Last updated:** 2026-05-16 (responsive table pass + encapsulation fixes + doc cleanup + PrimeNG conventions + PrimeNG component migration pass)
**Owner:** SK (Senior Software Architect)
**Scope:** Visual + interaction design only. Two portals (admin-app desktop-first, staff-app mobile-first PWA), one shared library. Code architecture is documented separately if/when needed.

> Markdown is not the right medium for true Figma-grade specs (states × breakpoints × variants explodes combinatorially), so this doc treats markdown as the source-of-truth for **tokens, components, state matrices, and wireframes**, and flags work that should eventually move into a Figma file.

### What changed since the previous revision

- **Tabs are now `<p-tabs>` everywhere** (log-shift, my-leave, leave admin, notifications, messaging, reports, announcements) — the previous `<button [class.active]>` pattern is gone.
- **Pagination is now `<p-paginator>`** in staff list, staff-detail work logs, announcements, log-shift history (was bare Prev/Next).
- **`<p-progressspinner styleClass="nc-spin">` is the standard loader** — `.nc-spinner` is now legacy. Skeletons still only used on the staff home.
- **Reports got a major rebuild** — 2 page tabs (Workforce Report / Payroll Export), custom multi-selects for staff/location/role, 5 result tabs, location bar chart, per-tab CSV download.
- **Notification bell + theme toggle** now have `pTooltip` and `aria-label` (fixes part of the a11y gap).
- **Staff list search** now uses `<p-iconfield>` with an inline `pi-search` icon.
- **Admin & staff messaging** are now fully built (was a placeholder before).
- **A new "action dropdown" pattern** has appeared in announcements + overtime — rendered outside `.nc-card` to escape `backdrop-filter` clipping. Parallel to the existing `<p-menu [popup]>` pattern in staff list — see gap **U26**.
- **Empty `projects/shared-lib/src/lib/components/help/`** directory present — feature in progress, not yet exported.
- Token values (sidebar widths, topbar heights, neon palette) and feature-flag keys are unchanged.

**PrimeNG migration pass (2026-05-16):**
- **Feature-flags toggle** — replaced hand-rolled `<label class="toggle-switch"><input type="checkbox">` with `<p-toggleswitch>` (consistent with overtime). Feature card icons migrated from emoji (`⏱ 💰 💵 📢 💬`) to `pi pi-*` class strings rendered via `<i [class]>`.
- **Admin notifications tabs** — replaced custom `<button [class.active]>` filter tabs with `<p-tabs>` (consistent with every other tabbed page in the app).
- **Leave type + status badges** — replaced all `<span class="leave-type-badge">` and `<span class="leave-status-badge">` with `<p-tag>` across admin leave, staff my-leave (type + status both).
- **Payroll leave-days pill** — replaced `<span class="leave-badge">` with `<p-tag severity="info">`.
- **Log-shift badges** — `<div class="active-badge">LIVE</div>` → `<p-tag severity="success" styleClass="live-tag">`; `<span class="badge-manual">` → `<p-tag severity="secondary">`.
- **Home live badge** — `<span class="live-badge">` → `<p-tag severity="success" styleClass="live-tag">`.
- **Leave balance bars** — replaced `[style.width.%]` hand-rolled bars with `<p-progressbar [showValue]="false">` (annual `bal-bar-annual`, sick `bal-bar-sick`); colors overridden via `:host :deep()`.
- **Reports location bars** — `<div class="loc-bar" [style.width.%]>` replaced with `<p-progressbar>`; gradient fill overridden via `:host :deep()`.
- All replaced SCSS blocks removed; orphaned classes cleaned up.

**Latest pass (2026-05-16):**
- **Responsive tables** — staff, overtime, payroll, and tips tables now stack as cards at ≤640 px. PrimeNG `responsiveLayout="stack"` with manual `<span class="p-column-title">` spans. `.p-column-title { display:none }` globally; `:host :deep()` re-enables them at mobile.
- **Angular `:deep()` encapsulation fix** — all responsive table SCSS migrated from `.[class] :deep(...)` (silently broken — class lives on PrimeNG's internal template div) to `:host :deep(...)` (anchors to the component host, works across child boundaries). See `CLAUDE.md` for the full explanation.
- **U8 ✓** — dialog width fixed to `min(440px, 92vw)` so it reflows on small phones.
- **U13 ✓** — `aria-label` added to all password eye-toggle buttons (admin + staff, login + set-password).
- **U20 ✓** — admin avatar link standardised to `/profile`; `/profile` alias route added alongside `/account`.
- **U23 ✓** — breadcrumb navigation added to staff detail page (`Staff › Marcus Torres`).
- **U24 ✓** — lang toggle now shows globe icon + **current** language label; tooltip says what it switches to.
- **U25 ✓** — leave calendar chips now show first name + last-initial (`Marcus T.`) to disambiguate duplicate first names.
- **U27 partial ✓** — overtime role field migrated from custom checkbox dropdown to `<p-multiselect appendTo="body">`. Reports custom multi-select still pending.
- **U30 partial ✓** — admin messaging compose form now shows a dynamic recipient hint (broadcast vs direct-message mode).

---

## Table of contents

- Part 1 — As-is UI ([§1 brand](#1-brand-identity-as-is), [§2 foundations](#2-foundations-as-is), [§3 patterns](#3-layout-patterns-as-is), [§4 components](#4-component-specs-as-is), [§5 admin screens](#5-screen-catalog--admin-app-as-is), [§6 staff screens](#6-screen-catalog--staff-app-as-is))
- Part 2 — [Gaps](#part-2--ui-gaps)
- Part 3 — [Target UI](#part-3--target-ui-design-figma-style)

---

# Part 1 — As-is UI

## 1. Brand identity (as-is)

| | |
|---|---|
| Product name | **NightCrew** (display name everywhere) |
| Logomark | `pi pi-bolt` lightning bolt, neon-purple, soft flicker animation |
| Wordmark | "NightCrew" in 800-weight, letterspaced 0.06em, gradient text purple→pink |
| Mood | After-hours / club: dark base, neon accents, glass surfaces, animated glow on focus |
| Two portals share the same brand. The login pages tag themselves as "Staff Portal" / "Management Portal" via subtitle. |

```
┌─────────────────┐
│  ⚡  NightCrew  │   ← logo + wordmark, sidebar header
└─────────────────┘
```

---

## 2. Foundations (as-is)

### 2.1 Color tokens

Tokens are declared as CSS custom properties on `:root` (dark mode default) and overridden under `[data-theme="light"]`. SCSS variables in `_theme.scss` resolve to these custom properties so theme changes propagate through view-encapsulated component styles.

**Surfaces — dark (default):**

| Token | Value | Use |
|---|---|---|
| `--nc-bg-void` | `#07070d` | Outermost shell background, behind everything |
| `--nc-bg-base` | `#0d0d16` | App background |
| `--nc-bg-surface` | `#13131f` | Cards, glass panels (with backdrop blur) |
| `--nc-bg-raised` | `#1a1a2a` | Inputs, table header rows, raised surfaces |

**Surfaces — light:**

| Token | Value | Use |
|---|---|---|
| `--nc-bg-void` | `#f0f0f8` | |
| `--nc-bg-base` | `#f5f5fc` | App background |
| `--nc-bg-surface` | `#ffffff` | Cards |
| `--nc-bg-raised` | `#eeeef8` | Inputs |

**Text — dark / light:**

| Token | Dark | Light | Use |
|---|---|---|---|
| `--nc-text-primary` | `#f0eeff` | `#1a1a2e` | Headings, body |
| `--nc-text-secondary` | `#a09ab8` | `#4b4870` | Subdued labels |
| `--nc-text-muted` | `#7b7698` | `#6b6885` | Hints, captions, metadata |

**Borders:**

| Token | Dark | Light |
|---|---|---|
| `--nc-border-subtle` | `rgba(168,85,247,0.12)` | `rgba(100,60,200,0.15)` |
| `--nc-border-glow` | `rgba(168,85,247,0.40)` | `rgba(120,60,220,0.35)` |

**Accents (identical in both themes):**

| Token | Hex | Use |
|---|---|---|
| `$neon-purple` | `#a855f7` | Primary brand, focus rings, active nav |
| `$neon-pink` | `#ec4899` | Wordmark gradient end, headline gradient end |
| `$neon-blue` | `#38bdf8` | Info accents |
| `$neon-green` | `#4ade80` | Success, live status, "On shift" |
| `$neon-amber` | `#fbbf24` | Warnings, labour cost emphasis |
| `$danger` | `#f87171` | Errors, destructive actions |

**Gradients:**

| Token | Definition | Use |
|---|---|---|
| `$grad-primary` | `linear-gradient(135deg, #7c3aed, #a855f7)` | Primary buttons, avatars, nav active indicator |
| `$grad-hero` | `linear-gradient(135deg, #4c1d95 0%, #831843 100%)` | Hero banners (login background blobs) |
| `$grad-card` | `linear-gradient(145deg, rgba(168,85,247,0.08) 0%, rgba(236,72,153,0.04) 100%)` | Default card fill on top of glass |

**Glows:**

| Token | Definition | Use |
|---|---|---|
| `$glow-purple` | `0 0 20px rgba(168,85,247,0.35), 0 0 60px rgba(168,85,247,0.10)` | Card focus, dialog edge |
| `$glow-green` | `0 0 16px rgba(74,222,128,0.40)` | Live badge dot |
| `$glow-sm` | `0 0 10px rgba(168,85,247,0.25)` | Input focus ring |

### 2.2 Typography

| | |
|---|---|
| Font stack | `Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif` (Inter configured as primary; system fonts as fallback) |
| Heading weight | 700 (h1–h4 set globally) |
| Heading line-height | 1.2 |
| Page title | 1.4 rem / 700 / on admin: gradient text purple→pink |
| Section title | 0.85 rem / 700 / uppercase, used inline (no consistent class) |
| Body | 0.875–0.95 rem |
| Stat value | 1.75 rem / 800 (admin dashboard cards) |
| Field label | 0.75 rem / 600 / uppercase / letter-spacing 0.06–0.07em |
| Caption / hint | 0.68–0.75 rem |
| Anti-aliasing | `-webkit-font-smoothing: antialiased` |

### 2.3 Spacing & radius

| Token | Value | Use |
|---|---|---|
| `$radius-sm` | 8px | Small chips, toggles |
| `$radius-md` | 12px | Inputs, buttons, nav items |
| `$radius-lg` | 18px | Cards, table cards, stat cards |
| `$radius-xl` | 24px | Hero containers |
| `$radius-pill` | 999px | Tags, pills, lang toggle |

Page padding: `.page-wrap` uses `1.25rem 1.5rem` (staff) / `1.5rem` (admin), collapsing to `1rem` at ≤768 px and `0.75rem` at ≤480 px. Field spacing inside cards is `0.4–0.9 rem` gap; section spacing is `1.25–1.5 rem`.

### 2.4 Elevation, glass, glows

Surfaces stack as:
1. **Void** (page background) — flat, animated radial-gradient blob fixed behind content.
2. **Glass** (`@mixin glass`) — `backdrop-filter: blur(12px)` + `rgba(19,19,31,0.7)` fill. Used by cards, sidebar, topbar.
3. **Neon border** (`@mixin neon-border`) — 1px `rgba(168,85,247,0.35)` + inset 1px highlight at 0.08 alpha.
4. **Glow** — applied selectively on focus and on the active-shift banner.

### 2.5 Motion

| Token | Duration | Easing | Use |
|---|---|---|---|
| `$transition-fast` | 0.15s | `ease` | Hover state, focus ring |
| `$transition-base` | 0.25s | `ease` | Sidebar collapse, nav indicator |

Named animations: `fade-up` (card enter), `slide-in-sidebar` (mobile drawer), `pulse-glow` (live dot), `neon-flicker` (logo, 6s), `spin` (loading), `nc-tooltip-in`, `menu-pop`. All concentrated in `styles/_animations.scss`.

### 2.6 Iconography

- **PrimeIcons** (`pi pi-*`) used throughout for functional icons — `pi-bolt`, `pi-bars`, `pi-power-off`, `pi-eye`, `pi-eye-slash`, `pi-bell`, `pi-megaphone`, `pi-sun`, `pi-heart`, `pi-wallet`, `pi-stopwatch`, `pi-map-marker`, `pi-clock`, `pi-arrow-right`, `pi-check`, `pi-times`, `pi-pencil`, `pi-exclamation-triangle`, `pi-chevron-up/down`, `pi-plus`, `pi-users`.
- **Emoji as nav-rail icons** in both shells: `◈ 📅 📢 🔔 💬 👥 📊 💰 📍 ⏱ 🏷 ◈ ⏱ 💵 🌴 👤`. (This inconsistency is called out in §gaps.)
- Icon sizes: nav 1.15 rem, header buttons 1.2 rem, stat-card icons 1.5–1.75 rem.

### 2.7 Z-index scale

| Layer | Z |
|---|---|
| background | 0 |
| content | 1 |
| topbar | 10 (staff) / 30 (admin) |
| overlay (mobile drawer scrim) | 40 |
| sidebar | 50 |
| dropdown | 200 |
| modal | 900 |
| dialog | 9000 |
| toast | 9999 |

### 2.8 Breakpoints

| Name | Width | Layout effect |
|---|---|---|
| Wide | > 1024 px | Default desktop layout |
| Medium | ≤ 1024 px | Stat grids start collapsing |
| Tablet | ≤ 768 px | Sidebar becomes drawer; topbar-user hidden; sign-out moves to drawer; `stat-grid` → 2 cols |
| Mobile | ≤ 480 px | `page-wrap` padding shrinks to 0.75rem; tables compact; `stat-grid` stays 2 cols (admin), `.page-content` width-clamped 480 px max (staff) |

---

## 3. Layout patterns (as-is)

### 3.1 App shell

Both portals use the same shell shape: fixed sidebar + sticky topbar + scrolling content.

```
Desktop (≥769 px)
┌────────────────────────────────────────────────────────────────────────┐
│┌────────┐┌──────────────────────────────────────────────────────────┐ │
││ Sidebar││ Topbar  [☰]              🔔  Marcus Torres  ☾  EN  ⎋   │ │
││        │└──────────────────────────────────────────────────────────┘ │
││ ⚡ NC  │┌──────────────────────────────────────────────────────────┐ │
││  «     ││                                                          │ │
││────────││                                                          │ │
││◈ Dash  ││                                                          │ │
││📅 Leave││                  Routed content (page-wrap)             │ │
││📢 Annc ││                                                          │ │
││🔔 Notif││                                                          │ │
││💬 Msgs ││                                                          │ │
││👥 Staff││                                                          │ │
││📊 Repts││                                                          │ │
││────────││                                                          │ │
││ MT     ││                                                          │ │
││ Marcus ││                                                          │ │
││ Admin  ││                                                          │ │
││ ⎋ Out  │└──────────────────────────────────────────────────────────┘ │
│└────────┘                                                              │
└────────────────────────────────────────────────────────────────────────┘
```

**Sidebar dimensions:**

| | Admin | Staff |
|---|---|---|
| Expanded width | 220 px | 240 px |
| Collapsed width | 64 px | 64 px |
| Topbar height | 60 px | 56 px |
| Nav-height token | — | 72 px (used for `.page-content` bottom padding offset) |

**Sidebar regions:** `sidebar-top` (logo + collapse button) · `sidebar-nav` (flex list, scrollable) · `sidebar-footer` (avatar + name/role + sign-out).

**Active nav item:** background `rgba(168,85,247,0.12)`, text `$neon-purple`, left-edge 3px gradient indicator with purple shadow.

**Mobile drawer (≤768 px):**
- Sidebar `display:none` by default; on `.shell:not(.collapsed)` it becomes a `position: fixed` overlay sliding in from left with `slide-in-sidebar` 0.2 s.
- A `0.5` alpha scrim covers the page behind (`shell::after`).
- Tapping any nav item triggers a `NavigationEnd` listener that re-collapses the drawer.

### 3.2 Page wrap

Two page-wrap variants:

| Variant | Used in | Padding |
|---|---|---|
| `.page-wrap` | Admin (always), staff (most pages) | 1.5rem (admin) / 1.25rem 1.5rem (staff), shrinks responsively |
| `.page-content` | Staff (clock-in, profile) | 1.25rem 1rem, `max-width: 480px`, centered, bottom-padded by `$nav-height` |

Page-fade-in animation on every page change (200 ms).

### 3.3 Page header

```
┌──────────────────────────────────────────────────────────────┐
│  PAGE TITLE (gradient on admin)        [Live ●] [+ Add Item] │
└──────────────────────────────────────────────────────────────┘
```

- Title left, primary action right.
- Title gradient (purple→pink) on admin only.
- Flex-wraps to a column at ≤768 px; primary action drops below title.
- Optional `.page-sub` (0.85 rem muted) on the next line.

### 3.4 Cards

`@mixin nc-card` is the canonical card:
- Glass background + neon border + `$grad-card` overlay
- Radius 18 px
- Padding 1.25 rem
- Enter animation `fade-up 0.25s`

Variants used in the codebase: `.stat-card` (4 accent classes — purple/green/pink/amber), `.empty-state`, `.state-card` (success/error), `.feature-card`, `.balance-card`, `.leave-card`, `.form-card`, `.table-card` (no padding, overflow:auto), `.warn-card`, `.recent-list`.

### 3.5 Stat grid

```
┌───────────┐ ┌───────────┐ ┌───────────┐ ┌───────────┐
│ ON SHIFT  │ │ TOT STAFF │ │ HOURS     │ │ LABOUR    │
│   12      │ │   48      │ │   84.2h   │ │  RM 1,260 │
│ clocked in│ │ active    │ │ logged    │ │ this shift│
└───────────┘ └───────────┘ └───────────┘ └───────────┘
```

CSS Grid: `repeat(auto-fit, minmax(160px, 1fr))`. Collapses to 2 cols at ≤768 px and stays at 2 cols at ≤480 px on admin.

### 3.6 Inline form pattern

Every "Add X" or "Edit X" surfaces an inline form card inside the page (no modal):

```
┌──────────────────────────────────────────────────────────┐
│  New Staff Member                                [Close] │
├──────────────────────────────────────────────────────────┤
│  * STAFF ID         * FIRST NAME       * LAST NAME       │
│  [ NC1001     ]     [ Marcus     ]     [ Torres     ]    │
│                                                          │
│  * EMAIL              ROLES                              │
│  [ a@venue.com ]    [ Bartender × Bouncer × ⌄ ]          │
│                                                          │
│  ...                                                     │
│                                                          │
│  * Note: Either IC or Passport is required               │
├──────────────────────────────────────────────────────────┤
│                                       [Cancel]  [Save]   │
└──────────────────────────────────────────────────────────┘
```

- `.form-card` (= `@nc-card`)
- `.form-header` flex with Close button right
- `.form-grid` CSS-grid `repeat(auto-fit, minmax(200px, 1fr))`, collapses to 1 col ≤480 px
- `.field` flex-column with uppercase label + input
- Required asterisk = `<span class="req">*</span>` (danger red)
- `.form-actions` right-aligned, top border, gap 0.75 rem

### 3.7 Table card

Tables live inside a `.table-card` (= glass card with `overflow-x: auto`) wrapping `<p-table>`:
- thead background `--nc-bg-raised`, columns 0.75 rem uppercase, color muted
- tbody rows hover `rgba(168,85,247,0.04)`, alternating not applied
- min-width 480 px so tables don't collapse below readable
- **≤640 px:** table collapses to a card-stack — each row becomes a bordered card, thead hidden, each `<td>` becomes `display:flex` showing a `.p-column-title` label beside the value. Pattern uses `:host :deep()` in component SCSS (not `.[class] :deep()` — see `CLAUDE.md`). Implemented in staff, overtime, payroll, tips.

**Pagination:** all paged tables use `<p-paginator>` bound by `[first]`, `[rows]`, `[totalRecords]` with an `(onPageChange)` handler. Used in staff list, staff-detail work logs, announcements table, log-shift history. No bare Prev/Next anywhere any more.

**Inline row actions:** a trailing `.td-actions` cell with a "Actions" ghost button. Two parallel patterns coexist (see gap U26):
1. `<p-menu [popup]="true" [model]="…" [appendTo]="'body'">` — staff list.
2. Custom dropdown rendered **outside** the `.nc-card` (so `backdrop-filter` doesn't clip it) with a hand-positioned `[style.top/right.px]` — announcements, overtime.

### 3.8 Card-list (alternative to table)

Used by leave records and staff messages — denser on mobile:

```
┌────────────────────────────────────────────────────┐
│ [annual]  [PENDING]                  [Edit] [Del]  │
│ Marcus Torres • NC1001                             │
│ 2026-05-20 to 2026-05-22                  [3d]     │
│ "Family event"                                     │
│                                  [✓ Approve][✗]    │
└────────────────────────────────────────────────────┘
```

### 3.9 Tab bar

Tabs are PrimeNG's `<p-tabs>` + `<p-tablist>` + `<p-tab>` with a signal-bound `[value]`. Used by every multi-mode page: log-shift (Log / History), log-shift mode (Start now / Manual), my-leave (Apply / History), leave admin (Records / Balances), notifications (All / Unread), messaging (Inbox / Sent), reports (Workforce / Payroll, then 5 result tabs), announcements admin if extended.

```
┌─────────────────────────────────────┐
│ [ Apply Leave ]  [ History  3 ]     │
│ ─────────────                       │  ← active indicator
└─────────────────────────────────────┘
```

- Count badges inline at the right of the label (e.g. `History 3`, `Inbox 2`).
- A separate `.tab-count` / `.tab-count.unread` class colours the badge.
- Tabs are used **inside pages**, never at the shell level.
- Some pages (reports) sit a download-CSV button beside the tablist in a `.tab-nav` flex row.

### 3.10 Filter bar

Sticky-feel card with multiple fields in a row:

```
┌──────────────────────────────────────────────────────────────────┐
│ STAFF       FROM        TO          STATUS                       │
│ [All ⌄ ]   [📅 …]      [📅 …]     [All ⌄ ]                       │
│ [Today][This Week][This Month]  [Annual: 12d][Sick: 4d]…         │
└──────────────────────────────────────────────────────────────────┘
```

Preset chips and summary pills wrap below the filter row.

### 3.11 Empty state

```
┌──────────────────────────────────────────────┐
│                                              │
│                     ☀                        │
│                                              │
│        No leave records found.               │
│                                              │
└──────────────────────────────────────────────┘
```

Centered icon (3 rem), 1.25 rem heading, 0.9 rem secondary copy. Border color shifts based on `.state-success` / `.state-error`.

### 3.12 Loading state

The canonical loader is now `<p-progressspinner styleClass="nc-spin">` at 18–32 px, centered in a wrapper such as `.loading-wrap` or `.loading-state`. The legacy CSS-only `.nc-spinner` class still exists in global styles but is no longer used by any feature.

- **Home (staff):** 4-card skeleton grid (the only place skeletons are used today).
- **Dashboard, leave, log-shift, reports, etc.:** `<p-progressspinner>` centered.
- **Buttons:** `[loading]="…"` PrimeNG primitive shows spinner inside the button (label flips to "Saving…" / "Loading…").
- **Inline section spinners** (e.g. staff-detail work logs while paginating) use the same `nc-spin` styleClass at 18 px.

### 3.13 Search field

Compact icon-prefixed input via PrimeNG `<p-iconfield>` + `<p-inputicon styleClass="pi pi-search">` wrapping a plain `pInputText`:

```
┌──────────────────────────────────────────────┐
│ 🔍  Search by name, ID or role…              │
└──────────────────────────────────────────────┘
```

Used today by the admin staff list. Should become the standard for any list page that needs free-text search.

### 3.14 Custom multi-select (reports filter)

Reports uses a hand-rolled multi-select instead of `<p-multiselect>` for staff / location / role filters. Three-part component:

```
┌─────────────────────────────────────┐
│  All staff (3 selected)        ▾    │  ← .ms-trigger button
├─────────────────────────────────────┤  ← .ms-panel (when open)
│  × Clear                            │
│  ☑ Marcus Torres        NC1001      │
│  ☑ Lila Kim             NC1002      │
│  ☐ Sam Goh              NC1003      │
└─────────────────────────────────────┘
```

Only one panel can be open at a time (the trigger closes its peers). See gap **U27** — this exists in parallel to `<p-multiselect>` used elsewhere and should be unified.

---

## 4. Component specs (as-is)

States documented per component: **default · hover · focus · active · disabled · loading · error**.

### 4.1 Button (`p-button`)

| Severity | Fill | Text | Border |
|---|---|---|---|
| primary | `$grad-primary` | white | none |
| secondary | transparent | secondary text | subtle |
| danger | `$danger` | white | none |
| success | `$neon-green` | dark | none |
| info | `$neon-blue` | dark | none |

Variants: `[text]="true"` (ghost), `[outlined]="true"` (border-only), `size="small"` (compact), `[loading]="…"` (spinner replaces icon), icon-only, label+icon.

| State | Visual |
|---|---|
| default | per severity above |
| hover | brightness +5%, subtle glow on primary |
| focus | 2 px outline `$neon-purple` (browser default — not custom) |
| active | scale not applied; depressed look implicit |
| disabled | opacity 0.5, cursor `not-allowed` |
| loading | spinner replaces label/icon, button stays width |

### 4.2 Text input / select / datepicker

Global rules in `styles.scss` apply to every `<input>`, `<select>`, `<textarea>`:
- Padding `0.75–0.8rem 1rem`
- Background `$bg-raised`, border 1 px `$border-subtle`, radius 12 px
- Font 0.9–0.95 rem
- Browser autofill chrome suppressed (`-webkit-autofill` box-shadow override)
- Native password reveal eye suppressed (Edge/Chrome/Safari rules)

| State | Visual |
|---|---|
| default | raised bg, subtle border |
| hover | (no separate hover) |
| focus | border `$neon-purple`, `$glow-sm` shadow |
| disabled | opacity 0.5 (PrimeNG default) |
| error | (no consistent styling — relies on `<p-message>` below the field) |
| readonly | login uses `readonly` then strips it on first interaction (anti-autofill trick) |

Custom select chevron is a purple SVG inline-data-URL, calendar picker indicator is colour-shifted via `filter: invert(0.5) sepia(1) saturate(3) hue-rotate(220deg)`.

### 4.3 Tag (`p-tag`)

Severities: `success` (green), `secondary` (muted), `info` (blue), `danger`. Used for: role chips on staff list, status pills (Active/Inactive/Pending/Approved/Rejected), event tags on dashboard ("In"/"Out"), live indicator.

### 4.4 Dialog (`p-dialog`)

- Modal scrim ≈ `rgba(0,0,0,0.6)`
- Width hardcoded per usage (`{width:'440px'}` reset-password, others vary)
- Header bar with title + close icon
- Body padded; footer right-aligns Cancel + primary

### 4.5 Toast / confirm

`<nc-toast>` and `<nc-confirm-dialog>` are mounted once at the shell root. Toast uses PrimeNG `MessageService`. Confirm is wrapped behind `ConfirmService.confirm(opts)` returning `Observable<boolean>` (custom).

### 4.6 Notification bell (`<nc-notification-bell>`)

Header icon (`pi pi-bell`) inside a `.bell-wrap` with a separate `.bell-badge` element showing `unreadCount()` (max "9+"). Click opens a PrimeNG `<p-popover>` with the panel layout below.

```
┌──────────────────────────────────┐
│ Notifications      Mark all read │
├──────────────────────────────────┤
│ 🔔  Leave approved               │
│     Your annual leave… · 2m ago  │
│ ─────────────────────────────    │
│ 💬  New message from Mgmt        │
│     Please confirm… · 1h ago  •  │ ← unread dot
│ ─────────────────────────────    │
│ (empty: "No notifications yet.") │
└──────────────────────────────────┘
```

Items use type-emoji icons from `iconFor(n.type)` (still emoji — see §gaps). Whole row is clickable when `routeFor(n)` returns a path. The bell button has `aria-label="Notifications"` and `pTooltip="Notifications"` with `tooltipPosition="bottom"`.

### 4.7 Theme toggle (`<nc-theme-toggle>`)

`<p-button>` ghost icon — `pi pi-moon` (dark) / `pi pi-sun` (light). Persists choice to `localStorage`. Sets `[data-theme="light"|"dark"]` on `<html>`. Has `aria-label="Toggle colour theme"` and `pTooltip` ("Switch to light/dark mode") on hover.

### 4.8 Sidebar nav item

| State | Visual |
|---|---|
| default | text secondary, icon greyscaled, no background |
| hover | bg `rgba(168,85,247,0.07)`, text primary, icon de-greyed |
| active | bg `rgba(168,85,247,0.12)`, text `$neon-purple`, 3 px gradient indicator left, icon full color |
| with badge | red/purple pill on the right showing unread count |
| collapsed | label hidden, icon centered, badge hidden |

### 4.9 Language toggle

Pill button in topbar (and login top-right). Shows a globe icon (`pi-globe`) + the **current** language label (`EN` when current is en, `中文` when current is zh). Tooltip says what it switches to (e.g. "Switch to 中文"). Triggers `LangService.toggle()`.

### 4.10 Avatar

32 px circle, `$grad-primary` background, white initials (2 chars), purple glow `0 0 10px rgba(168,85,247,0.4)`. Used in sidebar footer and as the "go to account/profile" target.

### 4.11 PrimeNG component conventions

One canonical component per use-case — no parallel patterns. New code must use the component in this table; deviations require a documented reason.

| Use case | Canonical component | Key attributes |
|---|---|---|
| Multi-select dropdown | `<p-multiselect>` | Always `appendTo="body"` inside any card with `backdrop-filter` |
| Single select / dropdown | `<p-select>` | Same `appendTo="body"` rule inside cards |
| Date picker | `<p-datepicker>` | `appendTo="body"`; add `PickerOnlyDirective` on mobile to disable keyboard |
| Row action menu | `<p-menu [popup]="true" [appendTo]="'body'">` | Do **not** use the custom-positioned dropdown (see U26) |
| Tabs | `<p-tabs>` + `<p-tablist>` + `<p-tab>` | No `<button [class.active]>` tabs anywhere |
| Pagination | `<p-paginator>` | Bind `[first]`, `[rows]`, `[totalRecords]`, `(onPageChange)` |
| Inline spinner (button) | `[loading]="saving()"` PrimeNG button primitive | |
| Section / page spinner | `<p-progressspinner styleClass="nc-spin">` | Not `.nc-spinner` (legacy) |
| Skeleton loading | `<p-skeleton>` | Use for >150 ms initial page loads |
| Modal / dialog | `<p-dialog>` | Width `min(440px, 92vw)` for small dialogs |
| Confirm dialog | `ConfirmService.confirm(opts).subscribe()` | Do **not** call PrimeNG `ConfirmationService` directly |
| Toast / alerts | `ToastService.success/error/warning/info()` | Do **not** call `MessageService` directly |
| Toggle switch | `<p-toggleswitch>` | |
| Tag / status badge | `<p-tag>` | Severities: `success` · `secondary` · `info` · `danger` · `warn` |
| Number input | `<p-inputnumber>` | |
| Text input | `pInputText` directive on `<input>` | |

---

## 5. Screen catalog — admin app (as-is)

> Wireframes show desktop unless `(mobile)` is noted. Components reference §4.

### 5.1 Login (`/auth/login`)

```
┌────────────────────────────────────────────────────────┐
│                                              ☾  EN    │
│                                                        │
│              ╔══════════════════════════╗              │
│              ║          ⚡              ║              │
│              ║      NightCrew           ║              │
│              ║   Management Portal      ║              │
│              ║                          ║              │
│              ║   STAFF ID               ║              │
│              ║   [ NC1001            ]  ║              │
│              ║   PASSWORD               ║              │
│              ║   [ ••••••••     👁  ]   ║              │
│              ║                          ║              │
│              ║   [─────  Sign In  ─────]║              │
│              ║                          ║              │
│              ║      Forgot password?    ║              │
│              ╚══════════════════════════╝              │
│                                                        │
│       (two blurred purple/pink blobs in background)    │
└────────────────────────────────────────────────────────┘
```

States: default · loading (spinner inside Sign In, label = "Signing in…") · error (red `<p-message>` above the button) · password-toggle (eye/eye-slash). Inputs start `readonly` to defeat autofill; first interaction removes the attribute.

### 5.2 Forgot password (`/auth/forgot-password`)

Same card chrome as login. Single field "STAFF ID" + primary button "Notify Manager". On success swaps to a confirmation card showing the success message and "Back to Sign In".

### 5.3 Set password (`/auth/set-password`)

Reached only when `mustChangePassword === true` after login. Same chrome. Fields: New Password, Confirm Password. Primary "Set Password". Includes the explanatory line `auth.tempPasswordInfo`.

### 5.4 Dashboard (`/dashboard`)

```
┌───────────────────────────────────────────────────────────────────┐
│  DASHBOARD                                      [● Live]          │
├───────────────────────────────────────────────────────────────────┤
│ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐                   │
│ │ON SHIFT │ │TOTAL    │ │TONIGHT  │ │EST.     │                   │
│ │   12    │ │STAFF 48 │ │HOURS    │ │LABOUR   │                   │
│ │ accent  │ │ accent  │ │ 84.2h   │ │RM 1,260 │                   │
│ │ green   │ │ purple  │ │ accent  │ │ accent  │                   │
│ │         │ │         │ │ pink    │ │ amber   │                   │
│ └─────────┘ └─────────┘ └─────────┘ └─────────┘ ┌───────────────┐│
│                                                 │⚠ 3 Open shifts││
│                                                 └───────────────┘│
│                                                                   │
│ CURRENTLY ON SHIFT                                                │
│ ┌─────────────────────────────────────────────────────────────┐   │
│ │ Staff ID │ Name        │ Role     │ Location │ In    │ Dur │   │
│ │ NC1001   │ Marcus T.   │[Bartender]│ Main Bar │ 21:04 │ 2h12│   │
│ │ NC1003   │ Lila K.     │[Bouncer]  │ Door     │ 20:50 │ 2h26│   │
│ └─────────────────────────────────────────────────────────────┘   │
│                                                                   │
│ RECENT CLOCK ACTIVITY                                             │
│ ┌─────────────────────────────────────────────────────────────┐   │
│ │ Staff    │ Role       │ Location │ Time    │ Event          │   │
│ │ Marcus T.│ [Bartender]│ Main Bar │ 21:04   │ [In]           │   │
│ │ Sam G.   │ [DJ]       │ Booth    │ 20:58   │ [Out]          │   │
│ └─────────────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────────┘
```

States: loading (text "Loading dashboard…") · live (green pulse-dot tag) · connecting (secondary tag) · empty activity (centered "No activity recorded today."). Newly-arriving rows pulse (`.row-new`).

### 5.5 Staff list (`/staff`)

```
┌───────────────────────────────────────────────────────────────┐
│ STAFF                                       [+ Add Staff]     │
├───────────────────────────────────────────────────────────────┤
│ [Add staff inline form when toggled — see §3.6]               │
│                                                               │
│ 🔎 [ Search by name, ID or role…                          ]   │
│                                                               │
│ ┌─────────────────────────────────────────────────────────┐   │
│ │ ID      │ Name / Email             │ Role        │ …    │   │
│ │ NC1001  │ Marcus Torres            │[Bartender]  │ ⋯    │   │
│ │         │ marcus@venue.com         │[Mgr]        │      │   │
│ │ NC1002  │ Lila Kim                 │[Bouncer]    │ ⋯    │   │
│ │         │ lila@venue.com           │             │      │   │
│ └─────────────────────────────────────────────────────────┘   │
│                                                               │
│             [Prev]   1 / 5 (47)   [Next]                      │
└───────────────────────────────────────────────────────────────┘
```

Row actions menu (Actions ▾) is `<p-menu [popup]>` with items: View Detail, Reset Password (opens dialog §5.6), Toggle Active, Delete (confirm). Phone numbers are masked (last 3 digits visible). Search field uses `<p-iconfield>` + `<p-inputicon pi-search>`. Pagination is `<p-paginator>`.

### 5.6 Reset password dialog

Modal width 440 px. Body: "Set a new password for **<name>**." + `<p-password>` (toggleable mask) + Generate Password ghost button. Footer: Cancel · Save. Disabled until `length >= 8`.

### 5.7 Staff detail (`/staff/:id`)

```
┌────────────────────────────────────────────────────────────────┐
│ [← Back]  Marcus Torres                       [✎ Edit]         │
│           NC1001                                               │
├────────────────────────────────────────────────────────────────┤
│ ┌─ Profile card ─────────────────────────────────────────────┐ │
│ │ FULL NAME      STAFF ID       EMAIL         PHONE          │ │
│ │ Marcus Torres  NC1001         marcus@…      +60 11-…       │ │
│ │ IC NO          PASSPORT       DOB           STATUS         │ │
│ │ —              A12345678      1995-01-15    [Active]       │ │
│ │ ROLES                                                      │ │
│ │ [Bartender] [Manager]                                      │ │
│ └────────────────────────────────────────────────────────────┘ │
│                                                                │
│ ┌─ Work log history ─────────────────────────────────────────┐ │
│ │ Date     │ Role      │ Location │ Start │ End  │ Hrs │ St │ │
│ │ 09 May   │ Bartender │ Main Bar │ 21:00 │23:30 │ 2.5h│Done│ │
│ │ 08 May   │ …         │ …        │ …     │ …    │ …   │ …  │ │
│ │                              [Paginator]                   │ │
│ └────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────┘
```

- Page header: breadcrumb nav (`Staff › Marcus Torres`) above the title (replaces old Back button), right-aligned Edit button.
- **Profile card:** auto-fill grid (`minmax(180px,1fr)`) of label/value pairs. Roles displayed as tag chips. Status as success/secondary tag. Edit mode swaps the same card for a two-column reactive-ngmodel form with the same fields, plus Save/Cancel.
- **Work log history:** `<p-table>` of past shifts (date · role · location · start · end · hours · status). Paginated via `<p-paginator>`. Inline spinner at 18 px while a page loads.
- Reset Password / Delete are reached from the staff list Actions menu, **not** this page.

### 5.8 Reports (`/reports`)

Two **page tabs** at the top via `<p-tabs>`: **Workforce Report** and **Payroll Export**. (Note: Payroll Export exists here _and_ as a standalone `/payroll` route — see gap U29.)

```
┌────────────────────────────────────────────────────────────────────┐
│ Reports                                                            │
│ [ Workforce Report ][ Payroll Export ]                             │
├────────────────────────────────────────────────────────────────────┤
│ ┌─ Filter bar ──────────────────────────────────────────────────┐  │
│ │ [Today][Yesterday][This Week][This Month][Last Month]         │  │
│ │ FROM 📅  TO 📅                                                │  │
│ │ STAFF [All ⌄ ]  LOCATION [All ⌄ ]  ROLE [All ⌄ ]  [Run Report]│  │
│ │   ^^ custom multi-select with checkboxes & Clear              │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                                                    │
│ ┌─ Summary stat grid (4 cards) ─────────────────────────────────┐  │
│ │ TOTAL SHIFTS │ TOTAL HOURS │ LABOUR COST │ UNIQUE STAFF       │  │
│ │   124        │   612.5h    │ RM 12,480   │   18 across 3 loc  │  │
│ │ accent purple│ accent pink │ accent amber│ accent green       │  │
│ └───────────────────────────────────────────────────────────────┘  │
│                                                                    │
│ ┌─ Result tabs ─────────────────────────────────┐ [⬇ Download CSV] │
│ │ [By Employee][By Location][By Role][By Date][All Shifts] │      │
│ ├──────────────────────────────────────────────────────────────┤   │
│ │ By Employee → table: staff ID · name · role · shifts ·       │   │
│ │              hours · avg/shift · gross pay                   │   │
│ │ By Location → bar-chart rows (loc-bar %width of max cost) +  │   │
│ │              shifts/hours/cost                               │   │
│ │ By Role     → table: role · staff count · shifts · hours ·   │   │
│ │              cost                                            │   │
│ │ By Date     → table: date · shifts · hours · cost            │   │
│ │ All Shifts  → table: staff · role · location · in · out ·    │   │
│ │              hours · cost · status · OT flag (warn tag)      │   │
│ └──────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

**Payroll Export tab:** single card with year (`<p-inputnumber>`) + month (`<p-select>`) + **Download Payroll CSV** button.

States: default · loading (button in-place spinner) · error (`<p-message severity="error">`) · empty (empty result tabs show "No data for selected filters"). OT shifts get a `<p-tag severity="warn" value="OT" pTooltip="Shift exceeds Xh">`.

### 5.9 Roles (`/roles`)

A table of system + custom roles with inline rate editing.

```
┌──────────────────────────────────────────────────────┐
│ ROLES                                  [+ Add Role]  │
├──────────────────────────────────────────────────────┤
│ Bartender    │ Custom │ RM 18.00/h  │ [Edit][Delete] │
│ Bouncer      │ Custom │ RM 16.00/h  │ [Edit][Delete] │
│ DJ           │ Custom │ RM 25.00/h  │ [Edit][Delete] │
│ admin        │ System │ —           │ (locked)       │
└──────────────────────────────────────────────────────┘
```

### 5.10 Locations (`/locations`)

Card grid (each location is a card with image, name, address, capacity, edit/delete). Add Location dialog with file upload for the image.

### 5.11 Leave (`/leave`)

The richest admin screen. See §3 wireframes plus:

- Top: balance summary cards (only when filter targets a single staff).
- Filter bar.
- Tabs: **Records** · **Balances**.
- Records tab has:
  - Overdue pending banner (expandable list of overdue with inline Approve/Reject/No-show).
  - Inline add/edit form (§3.6).
  - View toggle: **Table** · **Calendar**.
  - **Calendar view:** month grid with leave chips colour-coded by type. Click a day → bottom day-detail panel listing all leaves that day with inline Approve/Reject.
  - **Table view:** card list with status badges, type badges, dates, days pill, notes, inline Approve/Reject when pending.
- Balances tab: per-staff bar visualizer (annual/sick/unpaid) with inline entitlement editor; below it an all-staff overview list with annual/sick/unpaid chips (warning state when remaining ≤ 3).

### 5.12 Overtime rules (`/overtime`) — feature-flagged

```
┌────────────────────────────────────────────────────────────────┐
│  OT Rules                                       [+ Add Rule]   │
├────────────────────────────────────────────────────────────────┤
│ [Inline form when adding/editing]                              │
│   ROLE       [☑ All roles  ☐ Bartender  ☐ DJ … ▾]   ← custom   │
│   THRESHOLD  [ 8.0 ]   MULTIPLIER  [ 1.5x ]                    │
│   (edit mode adds:)  [●─] Active                               │
│                                            [Cancel] [Save]     │
│                                                                │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ Role         │ After Hours │ Multiplier │ Active │ …    │   │
│ │ All roles    │ 8h          │ 1.5x       │ [✓]    │ ⋯    │   │
│ │ Bartender,DJ │ 6h          │ 2.0x       │ [✓]    │ ⋯    │   │
│ └──────────────────────────────────────────────────────────┘   │
│                                                                │
│  ℹ Rules apply in order of specificity (role-specific beats    │
│    "All roles"). Inactive rules are ignored at calculation.    │
└────────────────────────────────────────────────────────────────┘
```

- Role field is `<p-multiselect appendTo="body">` — empty selection means "All roles (*)"; up to 2 chips shown, then "N roles". (Was a custom checkbox dropdown; migrated to p-multiselect.)
- Active flag in edit mode is `<p-toggleswitch>`.
- Row actions are the action-dropdown-outside-card pattern (§3.7 bullet 2).
- Footer is an inline `<p-message severity="info">` explaining how rules interact.

### 5.13 Announcements (`/announcements`) — feature-flagged

```
┌────────────────────────────────────────────────────────────────┐
│  Announcements                          [+ New Announcement]   │
├────────────────────────────────────────────────────────────────┤
│ [Inline form when adding/editing — single column]              │
│   *TITLE   [                                          ]        │
│   *BODY    [                                          ]        │
│            [                                          ]        │
│   TARGET ROLES  [ comma-separated…                    ]        │
│   EXPIRES AT    [ 📅 yy-mm-dd ]                                │
│                                            [Cancel] [Save]     │
│                                                                │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ Title       │ Target │ Reads │ Created by │ Expires │…  │   │
│ │ Closing time│ [All]  │ 28    │ Aisha M.   │ 12 Jun  │ ⋯ │   │
│ │ Roster note │ [DJ]   │  4    │ Aisha M.   │ —       │ ⋯ │   │
│ │                              [Paginator]                │   │
│ └──────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────┘
```

- Single-column inline form (NOT `.form-grid`).
- Target roles is currently a free-text comma-separated input — not a multi-select chip picker.
- Table columns: Title · Target · Reads · Created by · Expires · Active · Actions.
- Action dropdown uses the custom-outside-card pattern (Edit / Delete).
- No "Mine vs All" tab — admin manages all announcements in this single view; staff has its own `/announcements` (§6.9).

### 5.14 Tips (`/tips`) — feature-flagged

Pool list with date, total pot, distribution method (hours-weighted vs equal), per-staff payout preview, Distribute button (confirm dialog).

### 5.15 Payroll (`/payroll`) — feature-flagged

```
┌────────────────────────────────────────────────────────────────┐
│  Payroll                                                       │
├────────────────────────────────────────────────────────────────┤
│ ┌─ Filter card ──────────────────────────────────────────────┐ │
│ │ YEAR [ 2026 ]   MONTH [ May ⌄ ]    [View]                  │ │
│ └────────────────────────────────────────────────────────────┘ │
│                                                                │
│ Total Gross Payroll    RM 48,210.50           [⬇ Export CSV]   │
│                                                                │
│ ⚠ This period hasn't ended yet — data is incomplete. Active    │
│   shifts are excluded from payroll.       (current month only) │
│                                                                │
│ ┌──────────────────────────────────────────────────────────┐   │
│ │ ID    │ Name      │ Reg │ OT  │ Work Pay │ Lv Days│ Lv $ │   │
│ │ NC1001│ Marcus T. │ 84  │ 4.2 │  2,108.40│  [2d]  │ 288  │   │
│ │ …                                                        │   │
│ │  💡 Leave Pay = paid annual & sick leave days x 8h x     │   │
│ │     hourly rate. Unpaid leave is excluded.               │   │
│ └──────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────┘
```

- Filter card: year (`<p-inputnumber>` no grouping, 2020–2099) + month (`<p-select>`) + **View** button.
- Empty state (before clicking View): `pi-wallet` empty card "Select a year and month, then click View to load payroll data."
- Summary bar above the table: label + total + Export CSV ghost button.
- Conditional `<p-message severity="warn">` when the selected period is the current or a future month.
- Table columns: Staff ID · Name · Regular Hrs · OT Hrs · Work Pay (RM) · Paid Leave Days · Leave Pay (RM) · Grand Total (RM).
- Lightbulb footnote explaining the leave-pay formula.

**Note:** the same payroll-export action also exists inside `/reports` → "Payroll Export" tab — duplication called out as gap U29.

### 5.16 Messaging (`/messaging`) — feature-flagged

Not a two-pane thread view — it's compose + tabbed card list (matches the staff app).

```
┌────────────────────────────────────────────────────────────────┐
│ Messaging                                          [✎ Compose] │
├────────────────────────────────────────────────────────────────┤
│ [If composing:]                                                │
│ ┌─ New Message ─────────────────────────────────────────────┐  │
│ │ TO  [— All Staff (Broadcast) — / staff dropdown ⌄]        │  │
│ │ *MESSAGE                                                  │  │
│ │ [                                                       ] │  │
│ │ [                                                       ] │  │
│ │                                              123/2000     │  │
│ │                                       [Send Message]      │  │
│ └───────────────────────────────────────────────────────────┘  │
│                                                                │
│ [ Inbox  2 ]  [ Sent ]                                         │
│ ─────                                                          │
│ ┌─ Inbox card list ─────────────────────────────────────────┐  │
│ │ Marcus Torres                                          •  │  │ ← unread dot
│ │ "Can I swap Friday's shift with…"                         │  │
│ │ 09 May 2026, 09:42 am             [↩ Reply]               │  │
│ └───────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────┘
```

- **Compose:** recipient is `<p-select>` of active staff with a `[showClear]` (clearing = broadcast to all). Below the select, a dynamic hint line shows `pi-users` + "Broadcast — all staff will receive this message." when no recipient is selected, or `pi-user` + "Direct message — only the selected staff member will receive this." when one is selected. Hint turns amber in broadcast mode. Body is a 4-row `pTextarea` with a live `123/2000` char counter. Submit disabled while content is empty/whitespace.
- **Tabs:** `<p-tabs>` with `Inbox` (badge = unread count) and `Sent`.
- **Inbox card:** sender name (bold) · content · sent-at timestamp · Reply ghost button at right. Unread cards have an `.unread` modifier + a dot in the top-right. Clicking the card calls `markRead(msg)`.
- **Sent card:** "To: <recipient name | All Staff>" · content · timestamp.
- Empty states use `pi-inbox` (inbox) / `pi-send` (sent).

### 5.17 Notifications (`/notifications`)

Full-page version of what the bell shows:

- Page header: title + subtitle + "Mark all read" ghost button on the right (only visible when `unreadCount() > 0`).
- `<p-tabs>` filter: **All** (count badge) · **Unread** (count badge with `.unread` modifier).
- List inside a `.notif-list` card: row = emoji type-icon · title · message · `timeAgo(createdAt)`. Unread rows have `.unread` modifier and a trailing dot. Rows are `.clickable` when `routeFor(n)` resolves (and clicking navigates).
- Empty state uses `pi-bell`, copy varies between "You're all caught up!" (Unread) and "No notifications yet." (All).

### 5.18 Feature flags (`/features`) — admin-only + PIN

PIN unlock screen first (`/features/unlock`): single PIN input, error state on bad PIN.

After unlock:

```
┌──────────────────────────────────────────────────────────────┐
│ FEATURE MANAGEMENT                  [Enable all][Disable all]│
├──────────────────────────────────────────────────────────────┤
│ ┌────────────────┐ ┌────────────────┐ ┌────────────────┐     │
│ │ ✔ OT Rules     │ │   Tips         │ │   Payroll      │     │
│ │ enabled        │ │   disabled     │ │   disabled     │     │
│ │ [toggle ●─]    │ │ [toggle ─○]    │ │ [toggle ─○]    │     │
│ └────────────────┘ └────────────────┘ └────────────────┘     │
│ (more cards in a 3-col grid)                                 │
└──────────────────────────────────────────────────────────────┘
```

### 5.19 Account / change password (`/account`)

Form card with current/new/confirm password. Submit reuses `<nc-change-password>` from shared-lib.

---

## 6. Screen catalog — staff app (as-is)

> Staff app is mobile-first. Wireframes show **mobile** (≤480 px) primarily.

### 6.1 Login / forgot / set-password

Same chrome as admin (`<nc-toast>` shell-mounted, `pi pi-bolt` brand) — subtitle reads "Staff Portal" instead. Reuses `ForgotPasswordBase` and `SetPasswordBase` from shared-lib.

### 6.2 Home (`/home`)

```
Mobile (≤480 px)
┌──────────────────────────────────┐
│  Good evening, Marcus            │
│  BARTENDER                       │
│  📢 2 unread announcements →     │
├──────────────────────────────────┤
│ ┌──────────────────────────────┐ │
│ │ [LIVE]  BARTENDER            │ │
│ │ 📍 Main Bar  🕒 21:04 PM     │ │
│ │                Manage shift →│ │
│ └──────────────────────────────┘ │
│                                  │
│ ┌─────────────┐ ┌──────────────┐ │
│ │ ☀ ANNUAL    │ │ ♥ SICK       │ │
│ │ 9 days      │ │ 5 days       │ │
│ │ 3 / 12 used │ │ 1 / 6 used   │ │
│ └─────────────┘ └──────────────┘ │
│ ┌─────────────┐ ┌──────────────┐ │
│ │ 💼 GROSS    │ │ ⏱ HOURS      │ │
│ │ RM 2,108    │ │ 102.4h       │ │
│ │ this month  │ │ 4.2h OT      │ │
│ └─────────────┘ └──────────────┘ │
│                                  │
│ Recent shifts                    │
│ ┌──────────────────────────────┐ │
│ │ Fri 9 May | BARTENDER        │ │
│ │ 📍 Main Bar          8.0h    │ │
│ │ Thu 8 May | …                │ │
│ └──────────────────────────────┘ │
│                  View all →      │
└──────────────────────────────────┘
```

States: loading (4-card skeleton grid) · no-active-shift (replace banner with "Not clocked in" + Log Shift CTA) · pay/leave API down (muted "Data unavailable" cards) · no recent shifts (hint text).

### 6.3 Log shift (`/log-shift`)

Two top toggles: view = **Log Shift / History**, mode = **Start Now / Manual Entry**.

**Log Shift / Start Now:**

```
┌──────────────────────────────────┐
│ Log Shift                        │
│  [Log Shift]  [ History ]        │
│  [Start Now]  [ Manual ]         │
│                                  │
│ ROLE                             │
│ [ Bartender         ⌄ ]          │
│ LOCATION                         │
│ [ Main Bar          ⌄ ]          │
│ START TIME                       │
│ [ 21:04             📅]          │
│ NOTES (opt)                      │
│ [                     ]          │
│                                  │
│        [─── Start shift ───]     │
└──────────────────────────────────┘
```

**Active shift card (replaces form when a shift is in progress):**

```
┌──────────────────────────────────┐
│ [LIVE]  BARTENDER • Main Bar     │
│ Started 21:04 (2h 12m elapsed)   │
│ END TIME [ 23:16 📅 ]            │
│        [─── End shift ───]       │
└──────────────────────────────────┘
```

View + Mode toggles are both `<p-tabs>` (`Log` / `History` and `Start Now` / `Manual Entry`). Mode toggle only renders when no active shift is in progress.

**Manual entry mode:** adds Work Date field; validates duration > 0 and ≤ 24h; supports overnight crossover. Start/End time appear side-by-side in a 2-col sub-grid.

**History view:** card list (most recent first), each card shows role · date · location · time range · hours · optional manual badge · optional notes line · Delete button (confirm dialog). Pagination via `<p-paginator>`. Loading state is `<p-progressspinner>` centered; empty state uses `pi-list` "No shift history yet."

### 6.4 Pay summary (`/pay-summary`) — flagged

```
┌──────────────────────────────────┐
│ Pay Summary                      │
│                                  │
│ [This Week][This Month][Last Mo] │
│                                  │
│ ┌────────────┐ ┌────────────┐   │
│ │ 💼 GROSS   │ │ ⏱ REG HRS  │   │
│ │ RM 2,108   │ │ 84.0h      │   │
│ └────────────┘ └────────────┘   │
│ ┌────────────┐ ┌────────────┐   │
│ │ ⚡ OT HRS  │ │ 📋 SHIFTS  │   │
│ │ 4.2h       │ │ 9          │   │
│ └────────────┘ └────────────┘   │
│                                  │
│ Shifts this period               │
│ ┌──────────────────────────────┐ │
│ │ Fri 9 May | Bartender  8.0h  │ │
│ │ Main Bar  · 21:00 – 05:00    │ │
│ │ Thu 8 May | Bartender  6.5h  │ │
│ │ Main Bar  · 22:00 – 04:30    │ │
│ └──────────────────────────────┘ │
└──────────────────────────────────┘
```

- Date-range presets: **This Week / This Month / Last Month** chip row. Selecting a chip triggers an API call.
- 4 stat cards: Gross Pay (RM) · Regular Hours · OT Hours · Shifts Count.
- Shifts list below: card per shift showing role · date · location · time range · hours. No pagination (all for the period).
- Loading: `<p-progressspinner>` centered; empty: "No shifts in this period."
- Uses `.page-content` layout (max-width 480 px, centered).

### 6.5 Profile (`/profile`)

```
┌──────────────────────────────────┐
│             ●  MT                │
│       Marcus Torres              │
│  NC1001  ·  Bartender            │
├──────────────────────────────────┤
│ EMAIL                            │
│ marcus@venue.com                 │
│ PHONE                            │
│ +60 11-1234 5678         [Edit]  │
│ DATE OF BIRTH                    │
│ 1995-01-15                       │
│                                  │
│ [Language: English →]            │
│ [Theme: Dark →]                  │
│ [Change password →]              │
└──────────────────────────────────┘
```

Change-password is a separate route (`/profile/change-password`) hosting `<nc-change-password>`.

### 6.6 My leave (`/my-leave`)

```
┌──────────────────────────────────┐
│ My Leave                         │
│ ┌──────┐┌──────┐┌──────┐         │
│ │ANNUAL││SICK  ││UNPAID│         │
│ │ 9    ││ 5    ││ 0    │         │
│ │/12d  ││/6d   ││taken │         │
│ └──────┘└──────┘└──────┘         │
│                                  │
│ [ Apply Leave ]  [ History · 3 ] │
│ ────                              │
│                                  │
│ Apply Leave                      │
│ TYPE        [ Annual    ⌄ ]      │
│ FROM        [ 2026-05-20 📅 ]    │
│ TO          [ 2026-05-22 📅 ]    │
│ NOTES       [                 ]  │
│        [─── Apply leave ───]     │
└──────────────────────────────────┘
```

History tab: card list per leave (type badge, status badge, dates, days pill, notes, "Cancel" button while pending/upcoming).

### 6.7 Messaging (`/messaging`) — flagged

```
┌──────────────────────────────────┐
│ Messages              [✎ New]    │
│                                  │
│ [If composing:]                  │
│ ┌──────────────────────────────┐ │
│ │ To: Management               │ │
│ │ [                          ] │ │
│ │ [                          ] │ │
│ │                    42/2000   │ │
│ │              [📤 Send]       │ │
│ └──────────────────────────────┘ │
│                                  │
│ [ Inbox 1 ]  [ Sent ]            │
│ ─────                            │
│ ┌──────────────────────────────┐ │
│ │ From: Aisha M.            •  │ │
│ │ "Please confirm Friday's…"   │ │
│ │ 12 May 2026, 8:15 pm         │ │
│ └──────────────────────────────┘ │
└──────────────────────────────────┘
```

- Compose recipient is **fixed to "Management"** (staff can only message admins, not other staff). 4-row textarea + live char counter `count/2000`. Send disabled when empty.
- Tabs: Inbox / Sent via `<p-tabs>`.
- Inbox card layout matches admin (sender · content · sent-at · unread dot). Tap → `markRead`.
- Sent shows "To: Management" + content + sent-at.
- Uses `.page-content` (max-width 480 px, centered, bottom-padded by nav).

### 6.8 Tips history (`/tips-history`) — flagged

```
┌──────────────────────────────────┐
│ Tips History                     │
│                                  │
│ May 2026                         │
│ ┌──────────────────────────────┐ │
│ │ Pool: 9 May 2026             │ │
│ │ Role: Bartender              │ │
│ │ Hours: 8.0h (18% of pool)    │ │
│ │ Your share:      RM  72.00   │ │
│ └──────────────────────────────┘ │
│ ┌──────────────────────────────┐ │
│ │ Pool: 2 May 2026             │ │
│ │ Role: Bartender              │ │
│ │ Hours: 6.5h (14% of pool)    │ │
│ │ Your share:      RM  58.50   │ │
│ └──────────────────────────────┘ │
│                                  │
│ April 2026                       │
│ ┌──────────────────────────────┐ │
│ │ Pool: 25 Apr 2026  …         │ │
│ └──────────────────────────────┘ │
└──────────────────────────────────┘
```

- Card list of past tip distributions for this staff member, grouped by month header.
- Each card shows: pool date · role worked · hours contributed · percentage share of pool · payout amount.
- Hours weighting calculated by the server; percentage is displayed for transparency.
- Loading: `<p-progressspinner>`; empty: `pi-dollar` icon + "No tips distributions yet."
- Uses `.page-content` layout.

### 6.9 Announcements (`/announcements`) — flagged

```
┌──────────────────────────────────┐
│ Announcements                    │
│                                  │
│ ┌──────────────────────────────┐ │
│ │ ▌ Closing time update  [NEW] │ │  ← purple left bar = unread
│ │ Posted by Aisha M.           │ │
│ │ 12 May 2026, 8:00 pm         │ │
│ │                              │ │
│ │ [body expanded after tap]    │ │
│ │ All staff please note that   │ │
│ │ tonight's closing time is…   │ │
│ └──────────────────────────────┘ │
│ ┌──────────────────────────────┐ │
│ │   Roster note for DJ team    │ │  ← no bar = already read
│ │ Posted by Aisha M.           │ │
│ │ 10 May 2026, 3:15 pm         │ │
│ └──────────────────────────────┘ │
│                                  │
│  (empty: pi-megaphone            │
│   "No announcements yet.")       │
└──────────────────────────────────┘
```

- Card list, newest first.
- Unread cards have a 3 px left-edge purple bar (`.unread` modifier) and a `[NEW]` badge.
- Tapping a card expands the body inline and auto-marks it read on view (API `PATCH` call).
- No pagination — all current (non-expired) announcements loaded at once.
- Empty state: `pi-megaphone` + "No announcements yet."
- Uses `.page-content` layout.

### 6.10 Notifications (`/notifications`)

Same as admin §5.17.

---

# Part 2 — UI gaps

| # | Gap | Where | Why it hurts |
|---|---|---|---|
| U1 | **Emoji nav icons** | both shells (`◈ 📅 📢 🔔 💬 👥 📊 💰 📍 ⏱ 🏷 🌴 👤`) | Inconsistent per-OS render (Apple vs Segoe vs Noto); screen readers vocalize them as random Unicode names; doesn't match the rest of the app which uses `pi-*` icons. |
| U2 | **Sidebar widths differ between portals** (220 vs 240) | `_theme.scss` | No deliberate reason in code; reads as a typo to a new designer. |
| U3 | **Topbar heights differ** (60 vs 56 px) | `_theme.scss` | Mismatch when the two portals run side-by-side in QA. |
| U4 | **Light-mode is bolted on with `!important`** | `styles.scss` light overrides | Hard to extend; component-scoped styles override it back; new components forget to register. |
| U5 | **Focus rings rely on browser defaults** | global inputs/buttons | Inconsistent visual focus across PrimeNG components vs raw inputs; fails WCAG 2.1 focus visibility. |
| U6 | **Loading patterns still mix skeleton and spinner** *(partially fixed)* | home uses 4-card skeleton; every other page uses `<p-progressspinner>`; dashboard still has plain text "Loading dashboard…" | Decide where skeletons belong (>150 ms loads) vs spinners. |
| U9 | ~~**Tables aren't responsive**~~ *(fixed — staff, overtime, payroll, tips)* | card-stack layout at ≤640 px via `:host :deep()` + `.p-column-title` spans | open for dashboard "on shift" table |
| U10 | **Page header doesn't anchor primary action on mobile** | many pages | At ≤768 px the column-stack puts the CTA below the title in a way that's easy to miss; should pin to a sticky footer or move to a FAB. |
| U11 | **Mixed inline `style="..."` attributes** | dashboards, staff list, leave | Cannot be themed; defeats light-mode overrides; breaks design-token model. |
| U12 | **Gradient text on page titles has poor contrast** | admin `.page-title` | `-webkit-text-fill-color: transparent` over varied backgrounds; fails contrast on light theme. |
| U14 | **Required-field marker is colour-only** (red asterisk) | every form | Should be marker + accessible text ("required") for AT users. |
| U15 | **No keyboard shortcuts** | shell | Power users on admin can't paginate with arrow keys, open Add with `n`, etc. |
| U16 | **Anti-autofill `readonly` trick** strips usability | login | Some assistive tech reads the field as "read-only"; iOS users sometimes see no caret on first tap. |
| U17 | **Empty / error states inconsistent** | some pages have full state cards, others show plain `<p>` muted text | Cohesion problem. |
| U18 | **Toast position not specified** | shell `<nc-toast>` | Likely top-right (PrimeNG default), but the design doesn't say so — drift risk. |
| U19 | **Notification badge cap at "9+"** in admin | nav badge | Admin can plausibly have >9 unread; cap fine but should also affect topbar bell consistently. |
| U21 | **No skeleton for tables** | dashboard, staff, leave | Spinner-only feels janky on slow networks; PrimeNG `<p-skeleton>` is available. |
| U22 | **No FAB / sticky action on mobile** | staff-app pages | Long pages bury primary actions. |
| U23 | ~~**No breadcrumb on detail pages**~~ *(fixed — staff detail)* | `Staff › Marcus Torres` breadcrumb nav above the page title; remaining detail pages still open | — |
| U26 | **Two parallel action-menu patterns** | staff list uses `<p-menu [popup]>`; announcements / overtime use a custom dropdown rendered outside `.nc-card` to escape `backdrop-filter` clipping | Pick one. The "outside the card" workaround exists because `backdrop-filter` creates a containing block — needs documenting or replacing with a CDK overlay. |
| U27 | **Two parallel multi-select patterns** *(partial — overtime done)* | overtime role field migrated to `<p-multiselect appendTo="body">`; reports still uses hand-rolled checkbox dropdown | Unify remaining: reports staff/location/role selects. |
| U28 | **Empty `help/` directory** in shared-lib | `projects/shared-lib/src/lib/components/help/` is committed empty; no exports | Either build it or remove. |
| U29 | **Payroll export duplicated** | available at both `/payroll` (own route, with table + warn message) and `/reports` → Payroll Export tab | Pick the canonical surface; the other should redirect or hide. |
| U30 | **Broadcast mode not discoverable on admin messaging** *(partial)* | dynamic hint line now shows broadcast vs direct-message mode with icon + amber colour | Full radio-based UI still open |

---

# Part 3 — Target UI design (Figma-style)

The target keeps the night-club / neon identity that's already working but tightens consistency, accessibility, and responsive behaviour. Everything below should ultimately live in a Figma library; this section is the brief for that file.

## 7. Target principles

1. **One token system. Both portals consume it.** Sidebar / topbar dimensions identical; light & dark are siblings, not overrides.
2. **PrimeIcons everywhere — no emoji UI affordances.** Emoji is fine in content (announcements, messages) but never in navigation, status, or controls.
3. **Every interactive element specifies all 7 states.** default · hover · focus · active · disabled · loading · error. Focus state is visible, 2 px, brand-purple, 2 px offset.
4. **Mobile-first for staff-app, desktop-first for admin-app, but every admin page must be usable on a 390 px screen.** Tables become card-lists below 640 px.
5. **Loading discipline.** Skeleton for >150 ms loads, button-spinner for action waits, ARIA `aria-busy` on the affected region.
6. **Light mode is first-class, not an override.** All tokens defined twice (dark + light), no `!important` cascade.
7. **Accessibility = WCAG 2.1 AA.** Contrast ≥ 4.5:1 on text, focus rings visible, every icon-only button has `aria-label`, required fields marked text + colour.

## 8. Target design tokens

### 8.1 Unified surface tokens

```
--nc-bg-void       dark #07070d   light #f0f0f8
--nc-bg-base       dark #0d0d16   light #f5f5fc
--nc-bg-surface    dark #13131f   light #ffffff
--nc-bg-raised     dark #1a1a2a   light #eeeef8
--nc-bg-elev1      dark #1f1f30   light #fafaff   ← new: hover surface
--nc-bg-elev2      dark #25253a   light #ffffff   ← new: dialog floor
```

### 8.2 Brand & semantic

```
brand.primary   #a855f7  (== $neon-purple)
brand.gradient  linear-gradient(135deg, #7c3aed, #a855f7)
brand.accent    #ec4899
state.success   #4ade80
state.info      #38bdf8
state.warning   #fbbf24
state.danger    #f87171
state.neutral   --nc-text-secondary
```

### 8.3 Text

```
text.primary    contrast-checked vs --nc-bg-surface  →  dark 16.8:1, light 14.4:1
text.secondary  contrast-checked  →  dark 6.7:1, light 7.2:1
text.muted      contrast ≥ 4.5:1 for body, 3:1 for ≥18pt-bold
text.onPrimary  always #ffffff
text.link       brand.primary, underline-offset 2 px, underline on hover
```

### 8.4 Spacing scale (8-pt)

`4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 56 · 80` — replace today's ad-hoc rems.

### 8.5 Radius

Keep current scale (8 / 12 / 18 / 24 / pill). Codify "what gets which": chips=pill, tags=8, buttons=12, inputs=12, cards=18, hero=24.

### 8.6 Elevation

| Level | Use | Spec |
|---|---|---|
| `e0` | Inline (no shadow) | none |
| `e1` | Hover surface | dark: `0 1px 2px rgba(0,0,0,0.4)`; light: `0 1px 2px rgba(20,10,60,0.06)` |
| `e2` | Card | dark: `0 4px 12px rgba(0,0,0,0.5)`; light: `0 4px 12px rgba(20,10,60,0.08)` |
| `e3` | Dialog / popover | dark: `0 12px 32px rgba(0,0,0,0.6)`; light: `0 12px 32px rgba(20,10,60,0.12)` |
| `eg` | Brand glow (focus, live badge) | `0 0 0 2px rgba(168,85,247,0.40)` |

### 8.7 Motion

| Token | Duration | Easing | Use |
|---|---|---|---|
| `motion.instant` | 80 ms | linear | Toast in/out |
| `motion.fast` | 150 ms | cubic-bezier(0.4,0,0.2,1) | Hover, focus |
| `motion.base` | 200 ms | cubic-bezier(0.4,0,0.2,1) | Nav, expand/collapse |
| `motion.slow` | 320 ms | cubic-bezier(0.2,0,0,1) | Page enter |
| `motion.spring` | 360 ms | cubic-bezier(0.3,1.4,0.4,1) | Drawer slide |

Respect `prefers-reduced-motion: reduce` everywhere — disable `pulse-glow`, `neon-flicker`, page-enter; keep state transitions (≤ 120 ms).

### 8.8 Typography (target)

| Style | Family | Weight | Size | LH | Letter-spacing | Use |
|---|---|---|---|---|---|---|
| Display | Inter / system | 800 | 32 px | 1.15 | -0.02em | Login brand |
| H1 | Inter | 700 | 22 px | 1.2 | -0.01em | Page title |
| H2 | Inter | 700 | 18 px | 1.25 | 0 | Section title |
| H3 | Inter | 700 | 16 px | 1.3 | 0 | Card title, form section |
| Body | Inter | 400 | 14 px | 1.5 | 0 | Body |
| Body-strong | Inter | 600 | 14 px | 1.5 | 0 | Emphasis |
| Caption | Inter | 500 | 12 px | 1.4 | 0.02em | Hints |
| Overline | Inter | 600 | 11 px | 1.2 | 0.08em uppercase | Field labels, stat labels |
| Mono | JetBrains Mono | 500 | 13 px | 1.4 | 0 | Staff IDs, time, code |

(Bring in a hosted Inter to remove the system-font fingerprint differences between Win/macOS — keep the system stack as a fallback.)

## 9. Target component specs

For each component the spec is the matrix: **variant × size × state × theme**. The doc below lists the matrix headers; the full cell-by-cell visual lives in Figma.

### 9.1 Button

| Variant | Sizes | States |
|---|---|---|
| Primary (filled gradient) | sm 28 / md 36 / lg 44 px | default · hover · focus · active · disabled · loading |
| Secondary (outline) | same | same |
| Ghost (text-only) | same | same |
| Danger | same | same |
| Icon-only | sm 28 / md 36 / lg 40 | + `aria-label` required |
| FAB | 56 px round | + e3 elevation |

**Focus:** 2 px brand outline, 2 px offset; never `outline:none`.
**Loading:** label hidden, spinner centered, width preserved.

### 9.2 Inputs (text / number / select / multiselect / datepicker)

States: default · hover · focus · filled · disabled · readonly · invalid · success.

Visuals (dark theme):
- default: bg `--nc-bg-raised`, border `--nc-border-subtle`
- hover: border `--nc-border-glow`
- focus: border `brand.primary`, `eg` glow ring
- invalid: border `state.danger`, 8-px helper text below in danger
- success: border `state.success` (for inline validation, e.g. password meets policy)
- disabled: opacity 0.5, no hover effect

Pair every field with: label (overline style) · optional hint · error helper. Required marker = red `*` + `aria-required="true"` + visually-hidden "required" text.

### 9.3 Card

Variants: **Surface** (glass), **Solid** (raised, no blur), **Outline** (no fill), **Stat** (4 accent colors), **Empty/State** (centered icon + copy), **Form** (inline edit), **Warning** (amber border).

Each variant gets a 4-state hover/elev table.

### 9.4 Tag / chip

Sizes: sm (20 px), md (24 px). Variants: success · info · warning · danger · neutral · brand. Status-specific: `status-pending` (warning), `status-approved` (success), `status-rejected` (danger), `status-overdue` (warning + icon).

### 9.5 Tabs

Underline tabs only (single style). States: default · hover · active · disabled. Count badges inline at the right of the label. Keyboard: ←/→ moves between tabs, Home/End jumps.

### 9.6 Dialog / modal

Sizes: sm 400 px, md 560 px, lg 720 px, fullscreen ≤640 px. Sticky header with title + close (`pi-times`, `aria-label="Close"`). Footer right-aligned Cancel · Primary, reverse-order on Mac if we ever ship a desktop app.

### 9.7 Toast

Position: top-right desktop, top-center mobile. Variants: info · success · warning · danger. Max 3 stacked. Auto-dismiss 4 s (success/info), 6 s (warning), sticky (danger) with manual close. Always include an action where possible ("Undo", "Retry").

### 9.8 Sidebar nav item (target)

Replaces emoji icons with PrimeIcons. Final mapping:

| Path | Icon |
|---|---|
| `/dashboard` | `pi-th-large` |
| `/home` | `pi-home` |
| `/log-shift` | `pi-clock` |
| `/staff` | `pi-users` |
| `/leave`, `/my-leave` | `pi-calendar` |
| `/reports` | `pi-chart-bar` |
| `/tips`, `/tips-history` | `pi-dollar` |
| `/payroll`, `/pay-summary` | `pi-wallet` |
| `/locations` | `pi-map-marker` |
| `/overtime` | `pi-stopwatch` |
| `/roles` | `pi-id-card` |
| `/announcements` | `pi-megaphone` |
| `/messaging` | `pi-comments` |
| `/notifications` | `pi-bell` |
| `/account`, `/profile` | `pi-user` |
| `/features` | `pi-cog` |

### 9.9 Notification bell

Bell icon · badge top-right (cap "99+" not "9+") · panel 360 px wide / fullscreen on mobile · sections: "New" then "Earlier" · row = icon + title + body snippet + relative time · footer link "View all".

### 9.10 Empty / loading / error skeletons

| Context | Skeleton |
|---|---|
| Stat grid | 4 × stat-card skeletons |
| Table | 6 × row skeletons + header |
| Card list | 4 × card skeletons |
| Detail | section blocks for each detail group |
| Calendar | grid placeholders |

Every async region uses `aria-busy="true"` while loading and `aria-live="polite"` when data arrives.

## 10. Target navigation & IA

### 10.1 Information architecture

```
Admin app                              Staff app
  ├ Dashboard                            ├ Home
  ├ Staff ──┬─ List                      ├ Log shift  ──┬─ Live
  │         └─ Detail (tabs:             │              └─ Manual
  │            profile / docs / audit)   ├ My leave   ──┬─ Apply
  ├ Leave  ──┬─ Records (table/cal)      │              └─ History
  │         └─ Balances                  ├ Pay summary
  ├ Reports                              ├ Tips history
  ├ Roles                                ├ Announcements
  ├ Locations                            ├ Messaging
  ├ Overtime  *                          ├ Notifications
  ├ Tips      *                          └ Profile  ──┬─ Details
  ├ Payroll   *                                       ├ Language
  ├ Announcements *                                   ├ Theme
  ├ Messaging *                                       └ Change password
  ├ Notifications
  ├ Features  ** (admin only, PIN-locked)
  └ Account  ──┬─ Details
               └─ Change password
   * = feature-flagged       ** = role+PIN gated
```

### 10.2 Shell decisions (target)

| | Admin | Staff |
|---|---|---|
| Layout | Fixed sidebar 240 px + topbar 60 px (was 220) | Bottom nav on mobile (≤768 px), sidebar on tablet+ |
| Mobile nav | Drawer from left (overlay) | **Bottom tab bar (5 items)** + "More" drawer |
| Topbar | Logo · search (cmd-K) · notifications · theme · lang · user menu | Logo · notifications · theme · lang · profile shortcut |
| Avatar target | `/account` | `/profile` (rename both to `/me` long-term) |

Replacing the staff drawer with a bottom tab bar fixes the buried-CTA problem (U22) and matches user expectations on phones. Items: Home · Log shift · My leave · Notifications · More.

### 10.3 Cmd-K command palette (admin)

Add a global palette (`⌘K` / `Ctrl-K`) for: jump to staff by ID, jump to a leave record, open feature flags (if unlocked), toggle theme/language. Solves U15 partially.

## 11. Target screens (deltas only; full visuals in Figma)

Each screen below lists **what changes from as-is** and the **state matrix** the Figma frame must include.

### 11.1 Login (both portals)

- Drop the `readonly` autofill workaround; rely on `autocomplete="username"` / `autocomplete="current-password"` and `provideNoAutofill()` only where required.
- Eye toggle becomes `<button aria-label="Show password" aria-pressed="…">` (real button, not div).
- Add inline validation for staff-ID format.
- States: default · loading · error · success-redirect.
- Mobile: card fills viewport with 16 px outer padding; brand block size shrinks.

### 11.2 Admin dashboard

- Replace text "Loading dashboard…" with stat-card + table skeletons.
- "Open shifts" warning card moves into the stat grid as a permanent slot (shows "0" success-styled when none).
- Currently-on-shift table → card list at <640 px.
- Live badge promoted: top-right of page header gets a `LIVE` pill that pulses; SignalR reconnect surfaces a yellow `RECONNECTING` pill.

### 11.3 Staff list

- Search becomes a header-anchored sticky bar on mobile.
- Row Actions menu becomes an icon button (`pi-ellipsis-v`, `aria-label="Actions for <name>"`).
- Pagination: replace Prev/Next bare buttons with `<p-paginator>` and a "Rows per page" selector.

### 11.4 Staff detail

- Tabbed layout: **Profile · Documents · Activity · Settings**. Edit is per-tab (no inline-form-replaces-page).
- Header: avatar 64 px · name · staff ID · role tags · status pill · Actions menu (Reset password · Deactivate · Delete).
- Mobile: collapsible sections instead of tabs.

### 11.5 Reports

- Filter bar collapses into a "Filters" drawer trigger on mobile.
- Add CSV/Excel export buttons explicitly with a download icon.
- Empty state: "Pick a date range to run a report."

### 11.6 Locations

- Card grid → 1 col mobile, 2 col tablet, 3 col desktop.
- Each card has cover image, name, address, capacity, role-restriction chips.
- Image upload: drag-and-drop zone with preview + replace.

### 11.7 Leave (admin)

- Overdue banner becomes an inline alert at the top with severity warning, sticky during scroll on records tab.
- Calendar: localize the day-cell footprint to a single-week strip on mobile (current month grid is unusable on phone).
- Leave chips show staff initials + first-name to disambiguate (U25).
- Balances tab: each row keyboard-navigable; Enter → drill in.

### 11.8 Leave (staff "My leave")

- Move Apply form behind a primary "+ Apply" button so the default view is balances + history (more useful day-to-day).
- Date pickers default to today / +1 day; preset chips: "1 day" / "this weekend" / "next week".

### 11.9 Log shift

- Mode + view toggles become segmented controls (consistent with iOS / Material patterns), not pill buttons.
- Active shift card pinned to top of `/home` and `/log-shift` while live.
- "End shift" requires a confirm if the elapsed time < 30 min (likely an accident).
- Manual entry form gets a duration display ("3h 45m") that updates as user types.

### 11.10 Pay summary / Tips history

- Stat cards: gross / OT / net / take-home; secondary line shows period.
- Trend microchart in each card (sparkline of last 6 months).
- Tips history grouped by month with collapse/expand; each entry shows hours-weighted share calculation explicitly ("18% of pool").

### 11.11 Announcements

- Two surfaces: an unread strip in the staff home (already exists) and the full list.
- Cards on the list: title · severity stripe (info/warn/critical) · body preview · "X people read" footer (admin only).
- Read-on-view marks read after 2 s dwell time, not immediately on tap.

### 11.12 Notifications

- Two-column layout on desktop (list left, detail right).
- Group by today / yesterday / earlier.
- Each notification has a CTA (e.g. "View leave request").

### 11.13 Feature flags

- PIN unlock keeps the same form; failure has a 1-second cooldown and locks for 5 min after 5 wrong attempts.
- Toggle card: top-line feature name · description · current-rollout target ("All staff" / "Admin only" / "Pilot group") · last-changed timestamp + who.
- Add a search filter.

### 11.14 Account / change password

- Inline password-strength meter (Wide bar, color-graded; never red/green-only — use icon + color + label).
- Show requirements list with check-marks updating live.
- Confirm-on-submit dialog when current and new password are similar.

## 12. Responsive behavior summary

| Pattern | Wide | Tablet | Mobile |
|---|---|---|---|
| Sidebar | Fixed 240 px | Collapsible to 64 px | Drawer (admin) / bottom nav (staff) |
| Page header | Title + action inline | Same | Title above, action becomes FAB or sticky bottom CTA |
| Stat grid | 4 cols | 3 cols | 2 cols |
| Table | Sortable table | Sortable table | Card list |
| Form grid | 3 cols | 2 cols | 1 col |
| Calendar | Month grid | Month grid | Week strip + day list |
| Filter bar | Inline | Inline | "Filters" button → bottom sheet |
| Dialog | Centered 560 px | Centered 480 px | Fullscreen |

## 13. Accessibility commitments

- WCAG 2.1 AA on all screens.
- Contrast tokens defined and enforced in Stylelint.
- Every icon-only control has `aria-label`.
- Focus order matches visual order; no positive tabindex.
- Form errors announced via `aria-live="polite"`; success via `aria-live="assertive"` only for critical actions.
- Keyboard: every action reachable; `Esc` closes dialogs/drawers; ↑↓ navigate lists; Enter opens.
- Reduced motion respected.
- Screen-reader QA on the four golden flows: login, log shift, request leave (staff), approve leave (admin).

## 14. Migration plan (UI only)

| Step | Change | Status | Effort | Risk |
|---|---|---|---|---|
| 1 | Replace emoji nav icons with PrimeIcons (mapping in §9.8) | open | S | Low |
| 2 | Unify sidebar widths & topbar heights between portals | open | S | Low |
| 3 | Promote `<p-skeleton>` everywhere a spinner is used today (>150 ms loads) | partial — spinners unified to `<p-progressspinner>`; skeletons on home + staff list + tips history | M | Low |
| 4 | Convert dashboard "on shift" table to card-list <640 px | open (admin tables done; dashboard remains) | S | Low |
| 5 | Rebuild light mode as token-defined (no `!important` cascade) | open | M | Medium |
| 6 | Inter font · type scale refresh | open | S | Low |
| 7 | A11y pass + axe-core in CI | open | M | Low |
| 8 | Cmd-K palette (admin) | open | M | Low |
| 9 | Figma library publish, deprecate ad-hoc inline styles | open | L | Low |
| 10 | **Unify action-menu pattern** (U26) — settle on `<p-menu [popup] [appendTo]="'body'">` or CDK Overlay; retire the manually-positioned dropdown | open | M | Low |
| 11 | **Unify multi-select pattern** (U27) — replace reports' custom checkbox dropdowns with `<p-multiselect>` | partial — overtime done; reports open | M | Low |
| 12 | **Delete `/payroll` _or_ the Reports → Payroll Export tab** (U29) | open | S | Low |
| 13 | Remove legacy `.nc-spinner` from global styles (replaced by `nc-spin` styleClass) | open | S | Low |

Step 8 is still the only invasive one; everything else is incremental and safe to ship behind a feature flag.

---

## 15. Open questions

1. **Cmd-K palette** — nice-to-have or in scope for Phase 1?
2. **Admin app on mobile** — should we invest in true responsive (card-lists), or is "tablet+" acceptable?
3. **Calendar on staff mobile** — week strip + day list, or skip calendar view for staff?
4. **Empty-state illustrations** — keep PrimeIcon, or commission small neon line illustrations consistent with the brand?
5. **Dark default** — confirm we ship dark by default and follow OS preference on first run if no user choice yet.

---

## 16. References

- Tokens (current): [`projects/staff-app/src/styles/_theme.scss`](../projects/staff-app/src/styles/_theme.scss), [`projects/admin-app/src/styles/_theme.scss`](../projects/admin-app/src/styles/_theme.scss)
- Global styles: [`projects/staff-app/src/styles.scss`](../projects/staff-app/src/styles.scss), [`projects/admin-app/src/styles.scss`](../projects/admin-app/src/styles.scss)
- Shells: [`projects/staff-app/src/app/shell/shell.component.html`](../projects/staff-app/src/app/shell/shell.component.html), [`projects/admin-app/src/app/shell/shell.component.html`](../projects/admin-app/src/app/shell/shell.component.html)
- Representative screens: [`features/home/home.component.html`](../projects/staff-app/src/app/features/home/home.component.html), [`features/dashboard/dashboard.component.html`](../projects/admin-app/src/app/features/dashboard/dashboard.component.html), [`features/leave/leave.component.html`](../projects/admin-app/src/app/features/leave/leave.component.html)
- Project rules: [`CLAUDE.md`](../CLAUDE.md)
