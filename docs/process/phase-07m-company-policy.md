# Phase 7m: Company policy (working week and leave rules become settings)

> **Status:** `Draft`
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phase 7l merged (done). **Blocks Phase 8b** (the payroll calculator needs the working week and the unpaid-leave divisor from this policy).
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

**7m-b: frontend (about 1 day)** 5. `[TDD]` `CompanyPolicyService`, `leaveHours(start, end, startTime, endTime, schedule)` and vectors; both forms use the loaded policy. 6. Admin **Company policy** page (working days toggles, hours per day, notice days, max days, carry-over rule, divisor, live example), nav entry, permission gate. 7. E2E: switch to Mon-Sat, a Saturday leave is accepted and costs a full day; switch back. Docs: CLAUDE.md "Company policy" section replaces the hard-coded wording in "Leave rules"; ADR note.

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
