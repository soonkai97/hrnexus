# Phase 7m: Company policy (working week and leave rules become settings)

> **Status:** `7m-a merged (#105); 7m-b planned below`
> **Created:** 2026-11-13 | **Updated:** 2026-11-14
> **Depends on:** Phase 7l merged (done). **7m-b (the frontend half) is built on the single web app of Phase 7n**, so it starts after 7n. **7m-a (backend) blocks Phase 8b** (the payroll calculator needs the working week and the unpaid-leave divisor from this policy).
> **Why now:** the product must cater to different kinds of company (user, 13 Nov 2026): some work Monday to Friday and some Monday to Saturday; some leave is cumulative (unused days carry forward) and some is not. Phase 7l hard-coded one company's answers. This phase turns them into settings; **Mon-Fri, 8h, 3 days notice stay as the defaults**, so nothing changes for an installation that never opens the page.
> **Model:** one installation serves one company (as in the IMS standard). This is configuration, not multi-tenancy.

---

## Objectives

1. A company chooses its **working days** (any set of weekdays) and **hours per working day**; leave hours, balances, unpaid-leave deductions and every preview use that schedule.
2. A company chooses its **leave carry-over rule**: `none` (unused leave lapses), `capped` (up to N hours carry forward) or `unlimited` (cumulative: all unused hours carry forward).
3. The other leave limits that were constants become settings with the old values as defaults: annual-leave advance notice (days), longest single request (days).
4. The unpaid-leave **divisor** for payroll (default 26) lives in the same policy, ready for Phase 8.
5. Admins edit the policy on one admin page; every user's forms read it, so the live hour preview always matches what the server charges.
6. No behaviour change on an untouched installation (defaults equal the Phase 7l constants).

---

## What is hard-coded today (verified on `master`)

| Rule                                                         | Where                                                   | Becomes                                                               |
| ------------------------------------------------------------ | ------------------------------------------------------- | --------------------------------------------------------------------- |
| Working week is Monday to Friday                             | `LeaveHoursCalculator.WorkingDays`, FE `leave-hours.ts` | `WorkingDays` setting                                                 |
| 8 hours per working day                                      | `AppConstants.Leave.WorkdayHours`, FE constant          | `WorkdayHours` setting                                                |
| Annual leave needs 3 days' notice                            | `AppConstants.Leave.AnnualLeaveAdvanceDays`             | `AnnualLeaveAdvanceDays` setting                                      |
| A request cannot exceed 30 days                              | `UpsertLeaveCommand.MaxLeaveDays`                       | `MaxLeaveDays` setting                                                |
| Carry-over is a manual admin action capped at 14h by default | `CarryOverLeaveEntitlementCommand`                      | `CarryOverMode` + `CarryOverMaxHours`; the command follows the policy |
| Unpaid-leave divisor                                         | not built yet (Phase 8, default 26)                     | `UnpaidLeaveDivisor`                                                  |

---

## Design

- **`CompanyPolicy`** is a single-row table. Columns: `WorkingDays` (bit mask, Mon=1 ... Sun=64; at least one day), `WorkdayHours` (decimal, 1 to 24), `AnnualLeaveAdvanceDays` (0 to 60), `MaxLeaveDays` (1 to 366), `CarryOverMode` (`none` | `capped` | `unlimited`), `CarryOverMaxHours` (used when capped), `UnpaidLeaveDivisor` (decimal, 20 to 31), version token (`xmin`), audit fields. Seeded with the defaults.
- **`WorkSchedule`** value object (working-day mask + hours per day). `LeaveHoursCalculator` stays a **pure static class**; every method takes a `WorkSchedule`, so it is still unit-tested without a database. `WorkSchedule.Default` is Mon-Fri, 8h.
- **`ICompanyPolicyProvider`** returns the current policy (short in-memory cache, invalidated on save). Handlers that need it take the provider; nothing reads `AppConstants` for policy any more.
- **API:** `GET /api/company-policy` for **any signed-in user** (both apps need the working week to preview hours); `PUT /api/company-policy` needs the new `policy.manage` permission (admin portal, Setup group), refuses a stale version token, and writes an audit row.
- **Frontend:** a shared-lib `CompanyPolicyService` loads the policy once per session; `leaveHours()` takes a `WorkSchedule`; both leave forms use it. An admin **Company policy** page edits it and shows a live example ("a Fri to Mon request costs N hours").
- **Carry-over:** `none` carries nothing, `capped` carries `min(remaining, CarryOverMaxHours)`, `unlimited` carries all remaining. The admin "Carry over" action takes its rule from the policy (the old `maxCarryOver` body field is dropped). Carried hours stay in `CarryOverHours` (Phase 7l), so the base entitlement is never touched.
- **Changing the policy mid-year:** stored leave `Hours` stay authoritative; a leave created or edited afterwards uses the new schedule. Only the year split of a _multi-day leave that crosses New Year_ is recomputed from the current schedule (documented, low impact). Payroll runs keep their own calculation snapshot (Phase 8), so a locked month never changes.

---

## Scope

### In scope

- Backend: `CompanyPolicy` entity, migration with seeded defaults, provider, endpoints, `policy.manage` permission, calculator and balance/carry-over/advance-notice/max-days rewiring, audit.
- Frontend: policy service, `leaveHours()` with a schedule, the two leave forms, the admin policy page, nav entry.
- Tests: shared vectors extended with schedule variants; policy validation; E2E that switches the company to Mon-Sat and books a Saturday.

### Out of scope (deferred)

- Public holidays and company holiday calendars (needs its own phase; payroll and leave both consume it later).
- Per-employee or per-department schedules and shift patterns; part-time proration.
- Effective-dated policy history (single current policy in v1, see "Changing the policy mid-year").
- Carry-over expiry ("use within 6 months") and tenure-based entitlements.
- Multi-tenancy.

---

## Dependencies

| Dependency                                                                           | Type | Status                               |
| ------------------------------------------------------------------------------------ | ---- | ------------------------------------ |
| Phase 7l leave rules and the shared hour vectors (`tests/fixtures/leave-hours.json`) | Code | Done                                 |
| Permission catalog and role/user permission screens                                  | Code | Done (add `policy.manage`)           |
| Phase 8a (salary profile)                                                            | Code | In review; independent of this phase |

---

## Risks

| Risk                                                            | Likelihood | Impact | Mitigation                                                                                                                             |
| --------------------------------------------------------------- | ---------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| Backend and frontend disagree on the schedule math              | Med        | Med    | One fixture of vectors with schedule variants asserted by xUnit and vitest                                                             |
| A policy change silently rewrites history                       | Med        | High   | Stored hours stay authoritative; only the cross-year split of old multi-day rows reads the schedule; payroll snapshots its own figures |
| A bad policy (no working day, 0 hours) breaks leave and payroll | Low        | High   | Server validation of every field; seeded defaults; a stale-token refusal on save                                                       |
| A forgotten constant still encodes the old rule                 | Med        | Med    | Grep guard test for `WorkdayHours`/`AnnualLeaveAdvanceDays` constants outside defaults; tests on a Mon-Sat policy                      |
| Staff app shows hours before the policy loads                   | Low        | Low    | Policy loaded at app start with the defaults as fallback                                                                               |

---

## Implementation steps

**7m-a: backend (about 1.5 days)**

1. `[TDD]` `WorkSchedule` and `LeaveHoursCalculator` take the schedule; extend `tests/fixtures/leave-hours.json` with Mon-Sat, 4-day-week and 6-hour-day vectors; all existing vectors still pass with the default schedule.
2. `[TDD]` `CompanyPolicy` entity, migration (seeds defaults), `ICompanyPolicyProvider`, validation, `GET`/`PUT /api/company-policy`, `policy.manage`, audit.
3. `[TDD]` Rewire `UpsertLeaveCommand` (hours, advance notice, max days), balance queries and carry-over (`none` / `capped` / `unlimited`); the Phase 7l tests run again on the default policy and on a Mon-Sat policy.
4. Integration: policy persisted, stale token refused, `policy.manage` required, everyone can read.

**7m-b: frontend (about 1 day, on the single web app of Phase 7n; detailed plan in the next section)** 5. `[TDD]` `CompanyPolicyService` and `leaveHours()` / `leaveHoursInYear()` with a schedule, asserted against the shared vectors. 6. The two leave forms and calendars follow the loaded schedule. 7. Admin **Company policy** page at `/policy` (`policy.manage`). 8. E2E and docs.

---

## 7m-b detailed plan (frontend)

**Starting point (verified on `master` after 7n):** the backend is done: `GET /api/company-policy` returns `{ workingDays: number[] (ISO 1..7), workdayHours, annualLeaveAdvanceDays, maxLeaveDays, carryOverMode, carryOverMaxHours, unpaidLeaveDivisor, version }` for any signed-in user; `PUT` needs `policy.manage` (already in the catalog, the frontend `PermissionKey` union and the labels) and answers 400 for an invalid value and 409 for a stale `version` or any other refusal. The frontend still hard-codes Mon-Fri and 8 hours in `projects/shared-lib/src/lib/utils/leave-hours.ts`, and its spec asserts only the `calculate` and `hoursInYear` sections of `tests/fixtures/leave-hours.json`; the `scheduled` and `scheduledInYear` sections are asserted by the backend only. The two leave pages (`features/leave`, `features/my-leave`) call `leaveHours()`, say "Mon-Fri" in their error text, and grey Saturday and Sunday in their calendars with `getUTCDay() === 0 || 6`.

**Design**

- **`leave-hours.ts`:** `leaveHours(start, end, startTime, endTime, schedule = DEFAULT_SCHEDULE)` and `leaveHoursInYear(..., year, schedule = DEFAULT_SCHEDULE)`, where `WorkSchedule = { days: number[]; hours: number }` mirrors the fixture's `schedule`. `DEFAULT_SCHEDULE` is Mon-Fri, 8h, so every existing call and vector is unchanged. The single-day cap becomes `schedule.hours`; the working-day test reads `schedule.days` (ISO numbers) instead of "not Saturday or Sunday".
- **`CompanyPolicyService`** (shared-lib, `providedIn: 'root'`): a `policy` signal (defaults until loaded), a `schedule` computed, `load()` and `save(policy)`. The shell loads it once per session next to the permissions, so both leave pages already have the schedule when they open. Failure to load keeps the defaults and shows nothing (the server stays authoritative).
- **Leave pages:** the preview uses `policy.schedule()`; the error text becomes "Choose at least one working day and valid times."; the calendars grey the days that are **not** in the schedule (Saturday stays white for a Mon-Sat company).
- **Company policy page** (`features/policy`, route `/policy`, `permissionGuard('policy.manage', '/home')`, `unsavedChangesGuard`, menu item "Company policy" in the Setup group with `permission: 'policy.manage'`): seven weekday toggles, hours per day, annual-leave notice (days), longest request (days), carry-over mode (none / capped / unlimited, the cap field shown only for capped), unpaid-leave divisor, and a **live example** line ("A Fri to Mon request costs 8 hours", computed with the same `leaveHours()`). The template-driven `ngModel` pattern and skeleton load from CLAUDE.md apply. Client checks mirror the server (at least one day, ranges), the server message is shown on a 400, and a 409 asks the person to reload and shows the current values.
- **Non-retroactive wording** on the page: a one-line note that existing leave keeps its recorded hours.

**Steps (TDD where a rule is involved)**

1. `[TDD]` Extend `leave-hours.spec.ts` to assert the `scheduled` and `scheduledInYear` fixture sections (red), then add the `schedule` argument (green). Keep the existing 20 vectors passing. (~1 hour)
2. `[TDD]` `CompanyPolicyService` with `HttpTestingController`: loads, keeps defaults on error, `save()` PUTs the body with the loaded `version` and stores the new version. (~1 hour)
3. Wire both leave pages and calendars to the schedule; component specs: a Mon-Sat schedule makes a Saturday request valid with `workdayHours` hours; the default keeps it invalid. (~1.5 hours)
4. `[TDD]` Policy page component logic: form validity, the cap field only when capped, 409 handling, the live example. Then template and SCSS (card form, responsive at 360 px). (~3 hours)
5. Route, guard, menu item, shell menu spec (shown only with `policy.manage`), employee-bounce E2E list gains `/policy`. (~0.5 hour)
6. E2E `tests/e2e/admin/policy.spec.ts` against the rebuilt stack: an employee gets 403 on `PUT` and a redirect on `/policy`; the administrator opens the page and switches to Mon-Sat through the UI; a Saturday leave through the API is accepted and costs a full day while the same request is refused again after switching back; `afterAll` restores the defaults because the policy is global and the suite shares one installation. (~1.5 hours)
7. Docs: CLAUDE.md "Company policy" gets the frontend half and "Leave rules" stops saying Mon-Fri as a fact (it is the default); this plan's status; Phase 8 plan unchanged. (~0.5 hour)

**Risks specific to 7m-b**

| Risk                                                                     | Mitigation                                                                                                                    |
| ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------- |
| The E2E leaves the shared installation on Mon-Sat and breaks later specs | `afterAll` and a `finally` restore the defaults; the spec reads the current policy first and writes back exactly what it read |
| The form preview and the server disagree on a schedule                   | Same fixture asserted by vitest and xUnit; the server stays authoritative and its message is shown                            |
| A save from a stale page overwrites someone else's change                | The loaded `version` is sent back; a 409 reloads instead of overwriting                                                       |
| Hours show before the policy loads                                       | The service starts with the defaults and the shell loads it at start; the previews recompute when it arrives                  |

**Done when:** all vectors pass in both stacks, a Mon-Sat company can record Saturday leave from the UI, an employee cannot see or save the page, `ng test` (shared-lib and web), builds, ESLint, Prettier ratchet and E2E are green against the rebuilt stack, and CLAUDE.md describes the policy page.

**Model split (per the routing rule):** implementation on sonnet in one PR, about one day; a final review on opus because the change touches a contract the payroll calculator will rely on (the working week and divisor).

---

## Validation criteria

- [ ] Defaults reproduce Phase 7l behaviour exactly (all existing leave tests green unchanged)
- [ ] The shared vectors, including Mon-Sat, 4-day and 6-hour variants, pass in xUnit and vitest
- [ ] A Mon-Sat company can record Saturday leave; a Mon-Fri company still cannot
- [ ] Carry-over follows `none`, `capped` and `unlimited` (one test each, plus idempotency)
- [ ] Policy save is refused for invalid values and for a stale token; only `policy.manage` can save; every signed-in user can read
- [ ] `dotnet test`, `npx ng test` (three projects), builds, ESLint, Prettier ratchet, `dotnet format` clean; E2E green against the rebuilt stack
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge of the sub-phase PR.
- **Data:** the migration only adds the `CompanyPolicy` table (with one default row); its `Down` drops it. Defaults equal the old constants, so reverting loses nothing.

---

## Open questions

1. Is "cumulative" per leave type (annual only) or also sick leave? (Assumed annual only; sick leave lapses each year.)
2. Should carry-over run automatically on 1 January, or stay an admin action that follows the policy? (Assumed: stays an admin action in 7m; automatic carry-over is a small follow-up on the reset job.)
3. Is one policy per installation enough, or will one installation hold several companies? (Assumed one company per installation.)

---

## Review Notes

_Filled in after the phase completes._

| Issue | Perspective | Severity | Affects Phase | Resolution |
| ----- | ----------- | -------- | ------------- | ---------- |

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] CLAUDE.md leave rules and Phase 8 plan updated
