# Phase 7l: Leave fixes (make leave management correct)

> **Status:** `Complete` (PRs #98, #99, #100 and the review fixes; working week confirmed by the user 2026-11-13)
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phase 7 (shift log removed), 7j (Locations removed), 7i (English only), all merged. Independent of 7k (UI kit).
> **Environment note (13 Nov 2026):** no production deployment exists, so data migrations below need no backup or maintenance window yet.

Defects below were found by an audit on 2026-11-13 and **re-verified in code on the same day** (file and line references are to `master` at `a45ac61`).

---

## Objectives

1. A cancelled leave never blocks the same person from applying for those dates again.
2. Carry-over hours survive an entitlement edit (an admin changing the yearly entitlement cannot silently delete them).
3. Nobody can approve or reject their own leave.
4. Leave hours are computed by the server from working days (not from clock time), and the same rule is used by the web forms.
5. A request that crosses 31 Dec / 1 Jan is charged to each year's balance by the days that fall in that year.
6. Leave status changes follow an explicit state machine, and managers are told when staff withdraw approved leave.

---

## Verified defects

| #   | Defect                                     | Where (verified)                                                                                                                                                   | Effect                                                                                                                                                     |
| --- | ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| D1  | Overlap check excludes only `rejected`     | `UpsertLeaveCommand.EnsureNoOverlapAsync`: `l.Status != Rejected`                                                                                                  | Apply, cancel, re-apply for the same dates fails with "already exists overlapping". Balance queries already exclude `cancelled`, so the two rules disagree |
| D2  | Carry-over overwritten                     | `LeaveEntitlement.AddCarryOver` adds into `AnnualHours` and `CarryOverHours`; `LeaveEntitlement.Update(annualHours, ...)` then assigns `AnnualHours = annualHours` | Editing the yearly entitlement after carry-over drops the carried hours from the usable pool while `CarryOverHours` still shows them                       |
| D3  | Self-approval                              | `UpsertLeaveCommand` and `BulkUpdateLeaveStatusCommand` never compare the acting user with `EmployeeId`; admin endpoints need only `leave.approve`                 | A manager can approve or reject their own leave, or record their own leave as `approved`                                                                   |
| D4  | Hours are clock time and client-trusted    | `my-leave.component.ts` `calcHours` = end minus start in hours (a Mon 09:00 to Wed 18:00 request is 57h); the server only checks `Hours > 0` and `<= 720`          | Weekends and nights are charged; any client can send any number of hours                                                                                   |
| D5  | Cross-year leave charged to the start year | `EnsureSufficientBalanceAsync`, `GetLeaveBalanceQuery`, `CarryOverLeaveEntitlementCommand` all filter on `StartDate.Year`                                          | A 30 Dec to 3 Jan request is charged wholly to the old year; the new year's balance is overstated                                                          |
| D6  | No status state machine                    | `LeaveRequest.Update` accepts any status                                                                                                                           | A cancelled or rejected leave can be flipped back to approved by an edit, bypassing balance and overlap rules                                              |
| D7  | Withdrawal is silent                       | `CancelLeaveCommand` writes an audit row only                                                                                                                      | Managers are not told when staff withdraw approved leave                                                                                                   |

---

## Decisions made in this plan (change them here if wrong)

1. **Working-day model:** a working day is Monday to Friday and is **8 hours** (`AppConstants.Leave.WorkdayHours`). A multi-day request is `working days x 8`. A single-day request with times is the clock difference clamped to `0 < h <= 8`; without times it is 8. Public holidays are **not** modelled (needs a holiday calendar, deferred to Phase 8 with payroll).
2. **Self-approval is always blocked** on the admin endpoints. A user with `leave.approve` can record their own leave only as `pending`; another user with `leave.approve` must approve it. Consequence: an installation needs at least two people with `leave.approve`.
3. **Entitlement model:** `AnnualHours` becomes the **base** entitlement only; the usable annual pool is `AnnualHours + CarryOverHours`. `AddCarryOver` stops touching `AnnualHours`. Balance DTOs return the total pool as `AnnualEntitlement` plus `CarryOverHours`, so the staff UI is unchanged; the admin edit form edits the base value and shows carry-over beside it.
4. **State machine:** `pending -> approved | rejected | cancelled`; `approved -> rejected | cancelled`; `rejected` and `cancelled` are terminal (re-apply instead). Past-dated leave cannot be edited except to `cancelled`/`rejected` by an approver.
5. **Server owns hours:** the `Hours` field in request bodies stays for compatibility but the server ignores it and stores its own calculation; the API response returns the calculated value.

---

## Scope

### In scope

- Backend: `LeaveHoursCalculator` (pure, unit-tested), overlap rule, self-approval guard (`ICurrentUserService`), entitlement model change, per-year charging in balance / carry-over / sufficiency checks, state machine in `LeaveRequest`, manager notification on withdrawal.
- Migration `LeaveEntitlementBaseHours`: `UPDATE LeaveEntitlements SET AnnualHours = GREATEST(AnnualHours - CarryOverHours, 0)` (moves existing carry-over out of the base value). Down reverses.
- Frontend: shared-lib `leaveHours()` mirroring the server rule (same test vectors), admin and staff forms use it for the preview and stop sending authoritative hours; admin entitlement form shows base + carry-over; hide self-approval buttons in the admin leave list.
- Tests (TDD), E2E for D1 and D3, docs (CLAUDE.md leave rules).

### Out of scope (deferred)

- Public holidays and per-staff work schedules (Phase 8).
- Half-day granularity beyond the single-day time range.
- Backfilling historic `Hours` values (no production data; existing seed and E2E data are recreated).
- Leave types beyond the current five.

---

## Dependencies

| Dependency                                                                 | Type     | Status                                 |
| -------------------------------------------------------------------------- | -------- | -------------------------------------- |
| `ICurrentUserService` available to Application handlers                    | Code     | Done (used by `AuditService`)          |
| Integration suite (Docker, Postgres 17) for the Serializable balance tests | Infra    | Done                                   |
| Decisions 1 to 5 above                                                     | Decision | Assumed; revisit if the user disagrees |

---

## Risks

| Risk                                                                             | Likelihood | Impact | Mitigation                                                                                     |
| -------------------------------------------------------------------------------- | ---------- | ------ | ---------------------------------------------------------------------------------------------- |
| Existing tests assert client-supplied hours (many of the 64 `LeaveCommandTests`) | High       | Med    | Change tests to assert computed hours; keep one test per rule                                  |
| Concurrency guarantees weakened while changing the balance query                 | Low        | High   | Keep the Serializable transaction and `LoadConcurrencyTests`; add a cross-year concurrent case |
| Self-approval block locks a one-manager venue out of approving its own leave     | Med        | Med    | Documented consequence; error message names the rule; second approver required                 |
| FE and BE hour rules drift                                                       | Med        | Med    | One shared set of test vectors (JSON fixture asserted by both xUnit and vitest)                |
| Entitlement data migration miscalculates                                         | Low        | Med    | Migration test on a seeded DB; reversible                                                      |
| Scope creep into holidays/payroll                                                | Med        | Low    | Explicitly deferred                                                                            |

---

## Implementation Steps

Delivered as three PRs, each independently green. `[TDD]` = failing tests first.

**PR A: correctness quick fixes (D1, D3, D6, D7), about 0.5 day**

1. `[TDD]` D1: overlap ignores `cancelled`; tests: cancel then re-apply succeeds, rejected still re-appliable, approved/pending still block. S
2. `[TDD]` D3: guard in `UpsertLeaveCommand` and `BulkUpdateLeaveStatusCommand`: acting user equals `EmployeeId` and target status is approved/rejected (or create as non-pending) throws `InvalidOperationException` (409). Tests include bulk partial selection. S
3. `[TDD]` D6: transition table in `LeaveRequest`; tests for every allowed/forbidden transition; update admin FE to hide forbidden actions. M
4. `[TDD]` D7: `CancelLeaveCommand` notifies managers when an approved leave is withdrawn. S

**PR B: hours and year model (D4, D5), about 1.5 days** 5. `[TDD]` `LeaveHoursCalculator` (weekday count, single-day clamp, per-year split) with a shared JSON vector fixture. M 6. `[TDD]` Server computes hours in `UpsertLeaveCommand`; validation of range and time pairs; response returns computed hours. M 7. `[TDD]` Per-year charging in `EnsureSufficientBalanceAsync`, `GetLeaveBalanceQuery`, `CarryOverLeaveEntitlementCommand`; Serializable concurrency test across a year boundary. L 8. shared-lib `leaveHours()` + specs on the same vectors; admin and staff forms use it; stop sending authoritative hours. M

**PR C: entitlement model (D2), about 0.5 day** 9. `[TDD]` `AddCarryOver` only raises `CarryOverHours`; pool = base + carry-over in all readers; DTOs and admin form updated. M 10. Migration `LeaveEntitlementBaseHours` and an Integration test that applies it to seeded rows. S

**Closing** 11. E2E: cancel and re-apply; self-approval rejected; cross-year balance. S 12. CLAUDE.md leave rules section, phase sign-off, multi-perspective review. S

---

## Validation Criteria

- [x] Every defect D1 to D7 has at least one test that fails on `master` and passes after the fix
- [x] `dotnet test` green (Application, Infrastructure, Integration with the cross-year concurrency case)
- [x] `npx ng test` green for shared-lib, admin-app, staff-app; hour vectors shared between xUnit and vitest
- [x] Both apps build; ESLint and Prettier ratchet clean; `dotnet format` clean
- [x] E2E green against rebuilt Docker (cancel and re-apply, self-approval 409). Not done: an E2E for the cross-year balance; it is covered by unit tests and the Serializable Integration test instead
- [x] `has-pending-model-changes` reports none; migration applies on a seeded database
- [x] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge commit of the PR in question (A, B, C are independent except C's migration, which has a `Down`).
- **Data:** PR C's `Down` adds `CarryOverHours` back into `AnnualHours`. No other data is changed.

---

## Open questions

1. Is 8 hours Monday to Friday right for this venue? **Answered 2026-11-13: yes.**
2. Should a venue with a single manager be allowed a self-approval override? (Assumed no; a second approver is required.)
3. Do public holidays need to be excluded before Phase 8? (Assumed no.)

---

## Review Notes

Reviewed by an independent reviewer agent on 2026-11-13 after PRs A to C; findings fixed in the review PR.

| Issue                                                                                                                | Perspective       | Severity | Affects Phase | Resolution                                                                                                                                                                                                                         |
| -------------------------------------------------------------------------------------------------------------------- | ----------------- | -------- | ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| An update checked balance and overlap for the `employeeId` in the body, not the leave's owner                        | Security          | Medium   | 7l            | Fixed: the leave is loaded first; a different `employeeId` is a 400; checks use the owner                                                                                                                                          |
| Decision 4 half built: ended or closed leave could still be edited (retype an approved leave to refund annual hours) | Security, Product | Medium   | 7l            | Fixed: `rejected`/`cancelled` immutable; ended leave only changes status or becomes a no-show                                                                                                                                      |
| Mon-Fri model blocks weekend leave and any status edit of a weekend row; overnight spans charged by days             | Product alignment | Medium   | 8             | Status-only edits fixed (hours recomputed only when dates/times change). Open question 1 was **confirmed by the user on 2026-11-13: Mon-Fri, 8h**, so refusing a range with no weekday stands; overnight spans deferred to Phase 8 |
| `LeaveEntitlementBaseHours` Down is lossy and the planned migration test was not written                             | Maintainability   | Low      | 7l            | Down documented as lossy; no migration test (no production data, tiny one-off SQL)                                                                                                                                                 |
| Bulk no-show and the overdue-row No-Show button bypassed the self-approval rule                                      | Security          | Low      | 7l            | Fixed: own leave in a bulk no-show throws; terminal rows skipped; button hidden for own rows                                                                                                                                       |
| Withdrawal could report an error after the cancel committed (second save)                                            | Scalability       | Low      | 7l            | Fixed: notifications are saved in the same save as the cancellation                                                                                                                                                                |
| Cross-year apply now needs next year's entitlement to exist                                                          | Product alignment | Low      | 8             | Documented in CLAUDE.md leave rules; consider creating next year's entitlement in advance (admin bulk action exists)                                                                                                               |

## Sign-off

- [x] All validation criteria met (two deviations noted above and in the review notes)
- [x] Review completed (all 7 perspectives; see review notes)
- [x] CLAUDE.md leave rules updated
