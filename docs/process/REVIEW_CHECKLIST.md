# Multi-Perspective Review Checklist

After completing each implementation phase, review from all 7 perspectives below.
Record findings in the phase document's **Review Notes** section.
**Dynamically update subsequent phase plans** based on what you discover.

---

## How to Run a Review

1. Mark each item ✅ pass / ❌ fail / ⚠️ partial
2. For every ❌ or ⚠️: add an issue row in the phase doc's issue table
3. Determine which future phase the issue affects — update that phase's scope
4. If a critical issue is found: stop, fix, re-run the full checklist

---

## Perspective 1 — Architecture

- [ ] No circular dependencies introduced between shared-lib, admin-app, staff-app
- [ ] New abstractions are at the right level — not over-engineered for a single use
- [ ] Signal-based patterns consistent: signals for state, computed for derived, `inject()` for DI
- [ ] No new `NgModule` usage — strictly standalone components
- [ ] Breaking changes to shared-lib exports are documented
- [ ] No component does HTTP directly — goes through a service or store

## Perspective 2 — Scalability

- [ ] No N+1 patterns (calling API inside loops / in computed signals without memoisation)
- [ ] Pagination used where data sets can grow (staff list, work logs, etc.)
- [ ] No memory leaks — subscriptions cleaned up (`takeUntilDestroyed`, `async` pipe, or `DestroyRef`)
- [ ] Signal graphs don't chain excessively deep (>4 computed levels)
- [ ] Store mutations return Observables; components don't block the UI thread

## Perspective 3 — Security

- [ ] Every new route has `authGuard` + appropriate `roleGuard`
- [ ] No sensitive data (tokens, passwords, IC numbers) logged to console
- [ ] User input goes through validation before reaching the API
- [ ] No raw error objects exposed to the user — use `err?.error?.error ?? 'Generic message'`
- [ ] Password fields use `autocomplete="new-password"` or `"current-password"`
- [ ] No inline `eval()` or `innerHTML` bindings

## Perspective 4 — Maintainability

- [ ] All new code follows CLAUDE.md Rules 1–12
- [ ] No magic strings — use i18n keys, constants, or enums
- [ ] No commented-out code blocks left behind
- [ ] Component files are ≤300 lines — extract if larger
- [ ] SCSS uses design tokens (`$neon-purple`, `$bg-surface`, etc.) — no hardcoded hex values
- [ ] New i18n keys added to **both** `en.json` and `zh.json`
- [ ] PrimeNG `:deep()` rules use `:host :deep()` — not `.class-name :deep()`

## Perspective 5 — Testing

- [ ] Tests were written **before** implementation (TDD — if step was marked `[TDD]`)
- [ ] Tests verify **intent** (why the behavior matters), not just current output
- [ ] Loading state, error state, and empty state are all tested
- [ ] No `it('does something', () => { expect(true).toBe(true) })` placeholder tests
- [ ] Test coverage ≥70% for all files touched in this phase
- [ ] No `any` casts in test assertions
- [ ] E2E tests cover the happy path for any new user-facing flow

## Perspective 6 — Product Alignment

- [ ] Implemented feature matches the spec in the phase document exactly
- [ ] No scope creep — features not in this phase are filed as future items
- [ ] Mobile staff-app: tested on 375px width (iPhone SE viewport)
- [ ] Admin desktop: tested at 1280px width
- [ ] Loading skeletons present for all async data
- [ ] Consistent action patterns: confirm dialogs before destructive actions

## Perspective 7 — Performance

- [ ] Build output checked: no unexpected bundle size increase (>50 KB chunk)
- [ ] New lazy-loaded routes stay lazy (`loadComponent()` / `loadChildren()`)
- [ ] `computed()` signals don't perform expensive work (API calls, DOM reads)
- [ ] PrimeNG dropdowns/overlays inside cards use `appendTo="body"` to avoid z-index traps
- [ ] No `setInterval` or `setTimeout` left running without cleanup
- [ ] Images/icons use PrimeIcons — no base64 inline images

---

## Review Output Template

Copy into the phase document's Review Notes section:

| Issue | Perspective | Severity (P1/P2/P3) | Affects Phase | Resolution |
|-------|-------------|---------------------|---------------|-----------|
| [describe] | [1–7 + name] | [P1=blocker, P2=should fix, P3=nice to have] | [phase N] | [action] |
