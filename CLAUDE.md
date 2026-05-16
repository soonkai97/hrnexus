# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Angular 21 + PrimeNG 21 monorepo. Two apps: `projects/admin-app` (desktop-first, venue managers) and `projects/staff-app` (mobile-first PWA, venue staff). Shared component library in `projects/shared-lib`. .NET API backend. Deployed with Docker Compose (nginx serves both SPAs from a single container).

## Commands

```bash
# Full rebuild — always use for SCSS/TS changes; Docker layer cache skips Angular compile otherwise
docker compose build web --no-cache && docker compose up -d web

# Incremental build — only safe when you're sure Angular's build layer is already stale
docker compose build web && docker compose up -d web

# Local dev (no Docker)
npx ng serve admin-app     # localhost:4200
npx ng serve staff-app     # localhost:4201
```

After deploying, do a hard refresh (**Ctrl+Shift+R**) in the browser — nginx serves hashed bundles but the HTML shell isn't hashed.

## Architecture

### SCSS — critical gotcha

**Never use `.[class] :deep(...)` in component SCSS to pierce PrimeNG component boundaries.**

Angular compiles `.table-card :deep(.foo)` → `.table-card[_ngcontent-C] .foo`. When `table-card` is applied via `styleClass` on a PrimeNG component (e.g. `<p-card styleClass="table-card">`), that class lands on p-card's **internal template div** which carries p-card's own `_ngcontent` attribute — not the parent component's. The selector silently never fires.

**Use `:host :deep(...)` instead** — compiles to `[_nghost-C] .foo`, which correctly anchors to the component's own host element and reaches all descendants through child-component boundaries:

```scss
// ✗ broken — .table-card[_ngcontent-parent] never matches
.table-card :deep(.p-datatable-table) { display: block; }

// ✓ correct — [_nghost-component] always present on the host element
:host :deep(.p-datatable-table) { display: block; }
```

For PrimeNG overrides that need to work globally (e.g. hiding `.p-column-title` spans on desktop), add the rule to `projects/admin-app/src/styles.scss` instead of component SCSS.

### Responsive p-table stack layout

Tables switch to a card-stack at ≤640 px. The pattern lives in each component SCSS:

```scss
@media (max-width: 640px) {
  :host :deep(.p-datatable-table)  { display: block; }
  :host :deep(.p-datatable-thead)  { display: none; }
  :host :deep(.p-datatable-tbody > tr)    { display: block; ... }
  :host :deep(.p-datatable-tbody > tr > td) { display: flex; ... }
}
```

Each `<td>` must contain `<span class="p-column-title">Label</span>` before the cell value. `.p-column-title { display: none; }` is declared globally in `styles.scss`; the media-query block re-enables it via a nested `.p-column-title { display: block; }` inside the `:deep(td)` rule.

See `staff.component.scss` for the canonical implementation.

### PrimeNG overlays

Any overlay (`<p-menu>`, `<p-select>`, `<p-multiselect>`) inside a card that uses `backdrop-filter` must set `appendTo="body"`. Without it the overlay panel is clipped inside the card's CSS stacking context.

### Signals

All state uses Angular signals. No `BehaviorSubject` in new code:

```typescript
items   = signal<Item[]>([]);
loading = signal(false);
total   = computed(() => this.items().length);
```

### Design tokens

SCSS variables (`$neon-purple`, `$text-muted`, `$border-subtle`, `$radius-md`, …) live in `styles/_theme.scss` and resolve to CSS custom properties. Dark is the `:root` default; light overrides live under `[data-theme="light"]` in `styles.scss`. Every component SCSS opens with `@use 'styles/theme' as *;`.

### i18n

All UI strings use `{{ 'key' | translate }}`. Add new keys to both `assets/i18n/en.json` and `assets/i18n/zh.json` in each app.

### Design reference

`docs/design.md` — full design system spec, token values, component patterns, gap list (U1–U30), and migration plan.

---

## Process rules

These rules apply to every task in this project unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.

## Rule 1 — Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.

## Rule 2 — Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

## Rule 3 — Surgical Changes
Touch only what you must. Clean up only your own mess.
Don't "improve" adjacent code, comments, or formatting.
Don't refactor what isn't broken. Match existing style.

## Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

## Rule 5 — Use the model only for judgment calls
Use me for: classification, drafting, summarization, extraction.
Do NOT use me for: routing, retries, deterministic transforms.
If code can answer, code answers.

## Rule 6 — Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens.
If approaching budget, summarize and start fresh.
Surface the breach. Do not silently overrun.

## Rule 7 — Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

## Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

## Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

## Rule 10 — Checkpoint after every significant step
Summarize what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

## Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside the codebase.
If you genuinely think a convention is harmful, surface it. Don't fork silently.

## Rule 12 — Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.
