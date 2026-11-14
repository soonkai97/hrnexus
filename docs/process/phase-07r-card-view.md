# Phase 7r: Card view for every list screen

> **Status:** `Implemented` (7r-a to 7r-d; Leave records, the overdue panel and My Leave history were already card lists and became grids, so they have no separate Table view)
> **Created:** 2026-11-14 | **Updated:** 2026-11-14
> **Depends on:** Phase 7n (single web app), 7q (the Leave types card grid is the pattern)
> **Why:** the owner asked (14 Nov 2026) for "all pages in grid view or card view". Today the list screens are tables (Staff, Roles, Cash Advances, Payroll Locks, Payroll Rates), hand-built rows (Leave Requests, balances, summary) or both, and each looks a little different. Screenshots of the running app show sparse tables (Staff and Roles have three columns across 1,000 px) and a mobile "stack" hack that only exists below 640 px. Cards use the width better, read the same on phone and desktop, and one shared look removes the per-page differences.

---

## Objectives

1. Every list screen shows its records as **cards in a responsive grid** by default, with the same data and the same actions as the table.
2. Every list screen keeps a **Table** view for people who prefer it (bulk work, sorting), chosen with one toggle; the choice is remembered per screen on that device.
3. One shared look and one shared piece of code: the card, the grid and the toggle are defined once and used by every screen, so a screen cannot drift.
4. Nothing is lost: search, filters, sorting, pagination, bulk select and every row action work in both views; no API change.
5. Checked by looking at it: each converted screen is screenshotted at desktop and phone width before it is called done.

---

## Scope

### In scope

- Shared pieces (shared-lib or web `shared/`): a view-mode toggle with a small persisted store (`localStorage`, per screen key; works when storage is blocked), a card-grid layout and a card building block (header, meta lines, footer actions) as global styles plus a tiny presentational component, so each screen only decides what goes in its cards.
- Screens, in this order: Staff list, Roles, Cash Advances, Payroll Locks, Payroll Rates, Leave Requests (records and the overdue panel), Leave balances and Leave summary, My Leave history, Staff detail sections that are tables.
- A **sort** control for the card view (the table's column sort headers do not exist there), using the same sort options.
- Existing unit specs updated and new ones for the toggle and each screen's card helpers; E2E selectors that assume table rows are updated.

### Out of scope (deferred)

- Dashboard and Home tiles (already cards), calendars, the payroll run screens (Phase 8c builds them card-first with this pattern).
- Drag to reorder, saved views, per-user default view on the server.
- Redesigning forms and dialogs.

---

## Design

- **Default is Cards**, Table is the alternative. The toggle sits in the page toolbar next to the existing search and filters (two icon buttons with accessible names, `aria-pressed`).
- **Card anatomy** (the Leave types card is the model): a head (the main identifier and a status tag), one or two meta lines, optional chips, and a footer with the actions, pinned to the bottom so buttons line up across a row of cards. Equal-width cards in a `repeat(auto-fill, minmax(250px, 1fr))` grid; one column on a phone.
- **No content-dependent columns:** a card never sizes from its text, which is what made the Leave types rows misalign.
- **Persistence:** `ViewModeService.mode(key)` returns a signal; the value is stored under `view-mode:<key>`; every read and write is wrapped in try/catch and the screen works without storage.
- **Same data source:** cards render the array the table renders (for server-paged lists the store's current page), so paging, search and filters are untouched. Bulk select shows a checkbox on the card.
- **Mobile:** the card view is the phone layout, so the 640 px "stack" CSS for those tables is only kept for Table mode.

---

## Risks

| Risk                                                                    | Likelihood | Impact | Mitigation                                                                                                          |
| ----------------------------------------------------------------------- | ---------- | ------ | ------------------------------------------------------------------------------------------------------------------- |
| E2E tests assume table rows (`tbody tr`) and break on the Cards default | High       | Med    | Update selectors in the same PR as each screen; E2E runs for every screen PR (runtime behaviour changes)            |
| Pagination and bulk select behave differently between the views         | Med        | Med    | One data source for both views; a unit test per screen that both views show the same rows; E2E bulk select in Cards |
| A screen drifts from the shared look                                    | Med        | Low    | Card styles are global tokens, not per-screen copies; screenshots reviewed per screen                               |
| Large lists render many cards slowly                                    | Low        | Low    | Pagination already limits rows; `track` by id                                                                       |
| Large diff                                                              | Med        | Med    | One PR per sub-phase (below), each reviewable on its own                                                            |

---

## Implementation steps (about 4 to 5 days)

**7r-a: shared pieces, Staff, Roles (about 1 day).** `[TDD]` `ViewModeService` and the toggle (persistence, blocked storage, per-key); global card/grid styles; Staff list (cards with avatar initials, role chips, status tag, actions menu, bulk checkbox, sort select) and Roles (name, type tag, actions); specs; E2E selectors for staff and roles; screenshots.

**7r-b: finance lists (about 1 day).** Cash Advances (staff, amount, date, reason, status, mark settled / reopen / delete), Payroll Locks, Payroll Rates (a card per rate row with its verification state); specs, E2E, screenshots.

**7r-c: leave (about 1.5 days).** Leave Requests records and the overdue panel (the approve / reject / no-show actions stay one click), Leave balances and summary as cards, My Leave history list; specs, E2E, screenshots.

**7r-d: the rest and review (about 1 day).** Staff detail tables, any remaining list; `CLAUDE.md` pattern section; multi-perspective review (`REVIEW_CHECKLIST.md`), accessibility pass (names, focus order, `aria-pressed`).

---

## Validation criteria

- [ ] Every screen in scope shows cards by default and a Table on request; the choice survives a reload
- [ ] The same rows appear in both views; search, filters, sorting, paging, bulk select and all row actions work in both
- [ ] No screen has columns that shift with content; cards in a row have equal width and their actions line up
- [ ] Screenshots at 1280 px and 390 px reviewed for each converted screen
- [ ] Works with blocked storage (defaults to Cards, no error)
- [ ] `ng test` (shared-lib and web), ESLint, Prettier ratchet, production build; E2E green against the rebuilt stack
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the sub-phase PRs in reverse order; each screen keeps its table template, so a revert restores today's screens. No API, data or migration is involved.

---

## Questions for the owner

1. **Default view:** Cards for everyone (assumed), or Table on desktop and Cards on phone?
2. **Bulk work:** is Table the better view for bulk select on Staff and Leave Requests, so those two should default to Table?

---

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] `CLAUDE.md` pattern section added
