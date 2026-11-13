# Phase 8: Payroll rebuild (monthly salary, statutory deductions, payslips)

> **Status:** `Draft`
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phases 7, 7j, 7i, 7l merged (all done). Source of requirements: `HRNexus PRD v1.0`, section 5.4 (PAY-01 to PAY-10), section 6.2 (calculation order and run lifecycle), LEV-11, STF-05.
> **Environment note (13 Nov 2026):** no production deployment and no real data, so drop migrations and rate-table seeding need no backup yet. **Payroll go-live (not this phase's merge) is gated** by an accountant sign-off and a 3-month parallel run (PRD).

This phase is too large for one pass (Rule 13). It is delivered as **six sub-phases (8a to 8f)**, each one PR, each independently green. A later sub-phase starts only after the earlier one merged.

---

## Objectives

1. Pay is **monthly salary**, stored per person as an effective-dated history. Hourly pay is gone from the product (PRD non-goal).
2. A **payroll run** per month (draft, reviewed, approved, locked) calculates every payslip line by line in the PRD's fixed order, with exact decimals, and a locked run never changes.
3. EPF, SOCSO, EIS and PCB are computed from **rate tables stored as data with an effective date**, never hard-coded.
4. Staff read **only their own payslip** (PDF) in the staff app; salary is masked everywhere without a payroll permission.
5. Payroll locks and cash advances are folded into the run; unpaid leave and no-show flow in from the leave module.
6. Every figure on a payslip can be explained, and the rules exist as tests before the code (PRD risk control: "Payroll bug underpays staff").

---

## What exists today (verified on `master` at `e77ed4f`)

| Area                                                                         | State                                                                                                                                                   |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Role.HourlyRate`, `Role.PayType`, `RoleRate` (rate history)                 | Left from the shift-log era; **no consumer**. Hourly pay is a non-goal, so they are removed in 8a                                                       |
| `PayrollLock` (+ `PayrollLockGuard`, controller, admin page, `payroll.lock`) | Month locks with no payroll behind them; folded into the run in 8f                                                                                      |
| `CashAdvance` (+ commands, admin page, `cashadvance.manage`)                 | Records and settles advances; becomes a deduction source in 8c                                                                                          |
| Leave                                                                        | Stored in **hours** (Mon-Fri, 8h/day, Phase 7l); `unpaid` and `no-show` leave types exist. The PRD stores days: payroll converts `hours / WorkdayHours` |
| Permissions                                                                  | `payroll.view`, `payroll.manage`, `payroll.lock`, `cashadvance.manage` exist as catalog keys; no payroll behaviour behind them                          |
| Encryption                                                                   | `IFieldEncryptor` (AES-256-GCM) protects Staff PII; salary and bank details join the encrypted set                                                      |

---

## Decisions made in this plan (change them here if wrong)

1. **Monthly salary only.** Per-person `SalaryProfile` (effective-dated rows; the latest row with `EffectiveFrom <= month start` applies). Pay frequency stored (default monthly) but only monthly runs exist in v1.
2. **Money is `decimal` (MYR) end to end**, rounded per scheme (EPF rounds up to the next ringgit on the employee and employer share is calculated on the wage band; exact rules live with each rate table). No floating point anywhere, including the PDF.
3. **Rate tables are data.** `StatutoryRateTable` rows (scheme, effective-from, wage base rule, ceiling, employee rate, employer rate, band table where the scheme uses one). Seeded from the PRD figures but every row carries `Verified = false` until the accountant confirms; a payroll cannot be **approved** while any table it used is unverified (guard in 8b/8c).
4. **Calculation order is fixed** (PRD 6.2): prorated basic, unpaid-leave deduction, statutory, other deductions, rounding. Each step takes only earlier output so a payslip explains itself.
5. **Unpaid-leave deduction** = monthly basic / **divisor** x unpaid working days, capped at the prorated basic. Divisor is a company policy value, default **26** (UNCONFIRMED; printed on the payslip).
6. **Run lifecycle** exactly as the PRD table (start, edit/recalculate, submit, approve by a _different_ person, return, lock, reopen only before any bank or statutory file was exported, corrections go to the next run). Version token on the run; a stale save is refused with an error code.
7. **Single-person exception:** if only one user holds payroll permission the owner approves after signing in again; the run and payslip register are flagged "single-person approval" (PRD).
8. **Permissions:** `payroll.view` (see runs and salary), `payroll.manage` (prepare, edit, submit, lock), `payroll.approve` (new; approve and return), `payroll.reopen` (new; separate by PRD), `payroll.export` (new). Staff see only their own payslips through `/api/me/payslips` (authentication only, filtered by caller id).
9. **Overtime and other pay are hand-entered with a reason** (PAY-04); no automatic overtime in v1. A per-person overtime-rule field is deferred until the owner answers PRD question 3.
10. **No electronic filing.** v1 produces the bank file and statutory reports; a person submits them (PRD non-goal).
11. **Leavers and joiners** are prorated by calendar days; a deactivated person stays in that month's run until final pay is locked.

---

## Sub-phases

### 8a: Salary profile and employment details (about 1 day)

- `SalaryProfile` entity (employee, monthly basic, frequency, effective-from, bank name/account/holder, created by). Salary and bank fields encrypted at rest.
- STF-05 fields needed by payroll only: citizenship status (citizen or permanent resident; the only EPF/SOCSO class in scope), employment start date, optional end date. Job title, department, probation are deferred to the staff-profile phase.
- Admin UI on the staff detail page (visible with `payroll.view`; masked otherwise, PAY-09). API returns salary only to `payroll.view` holders.
- **Remove** `Role.HourlyRate`, `Role.PayType`, `RoleRate` and every consumer (roles page, DTOs, E2E); migration `DropRolePayFields`.
- Tests: effective-dating lookup, history is append-only, masked for users without the permission, encryption round trip.

### 8b: Statutory rate tables and the calculator (about 1.5 days)

- `StatutoryRateTable` entity and seed (EPF 11% employee, 13% employer up to RM5,000 of wages and 12% above; SOCSO category 1 0.5% / 1.75%; EIS 0.2% / 0.2%; SOCSO and EIS ceiling RM6,000 from 1 Oct 2024; HRD Corp 1% employer, informational). All `Verified = false`.
- Pure `PayrollCalculator` (no database): inputs are the person's month facts, outputs are payslip lines. PCB is a **pluggable table lookup** (LHDN schedule imported as data) because the PRD names no formula; until a schedule is loaded PCB is 0 with a visible warning and the run cannot be approved.
- Tests first from hand-worked examples (EPF at and across RM5,000, SOCSO at the RM6,000 ceiling, proration for a mid-month joiner and leaver, unpaid-leave cap, deduction cap and carry-over) written with the accountant's sign-off in mind.

### 8c: Payroll run and inputs (about 2 days)

- `PayrollRun` (month, state, version token, preparer, approver, single-person flag, export status) and `PayrollRunLine` (one per person, with typed lines: basic, allowance, overtime, unpaid-leave deduction, EPF/SOCSO/EIS/PCB employee and employer, cash-advance repayment, adjustment).
- Allowances and deductions (PAY-02) as `PayItem` rows (fixed or one-off, per person), including cash-advance repayment schedule; manual overtime/other pay entered with a reason.
- Leave inputs (LEV-11): approved `unpaid` and `no-show` leave in the month, converted from hours to days; a change touching a locked month creates an **adjustment line** in the next open run.
- Lifecycle commands with guards and audit rows; "no negative net pay" check on submit; people without a bank account carry a payment method on the run.
- Tests: every row of the lifecycle table (allowed and refused), stale version token, preparer cannot approve, Serializable arbitration for two people editing one run.

### 8d: Payslip PDF and staff view (about 1.5 days)

- Payslip generation (server-side PDF) with the divisor used and a line-by-line explanation; payslip numbering `YYYYMM-<staffId>`; stored on lock, hidden again on reopen.
- Staff app page `/payslips` (own only) and admin "payslip register" (with the single-person flag).
- Tests: a staff member can never fetch another person's payslip (BOLA), PDF contains the right figures.

### 8e: Exports (about 1 day, P1)

- Payroll summary CSV; statutory submission reports (EPF, SOCSO, EIS, PCB listings); bank payment file. **The bank format is an open question (PRD Q3)**; ship a generic CSV and a format interface, add the bank's layout when known. Exports are generated before lock and flip the "exported" flag that forbids reopening.
- CSV formula-injection protection on every exported text cell.

### 8f: Fold in locks and cash advances, close out (about 0.5 day)

- `PayrollLock` and its page/permission are replaced by the run's `locked` state; the `payroll.lock` key is retired (migration cleans grants). Cash advances stay a page but their repayments are run inputs.
- E2E: start run, edit, submit, approve (second user), lock, staff reads the payslip, reopen refused after export.
- CLAUDE.md payroll rules, README, ADR for the money and rate-table decisions, sign-off, multi-perspective review.

**PAY-10 (year-end statement / EA form) is deferred** (P2; layout still to be confirmed).

---

## Scope

### Out of scope (deferred)

- Hourly pay, attendance, automatic overtime, foreign workers, non-citizen rates, Sabah/Sarawak, electronic filing, EA form, termination pay rules, public-holiday work pay, part-time proration beyond calendar days, probation effects, age-based SOCSO/EIS exemptions (PRD 6.6: to be specified; 8b leaves hooks in the table design).
- Public holidays in the unpaid-leave count (Phase 7l has no holiday calendar; revisit with the accountant).

---

## Dependencies

| Dependency                                                                          | Type     | Status                                                       |
| ----------------------------------------------------------------------------------- | -------- | ------------------------------------------------------------ |
| Phase 7l leave rules (Mon-Fri, 8h, hours stored)                                    | Code     | Done                                                         |
| Accountant or payroll specialist to verify rates, rounding, divisor, section 24 cap | External | **Pending** (blocks approval of any real run, not the build) |
| LHDN PCB schedule as data                                                           | External | **Pending**                                                  |
| Company bank payment file layout                                                    | External | **Pending** (PRD Q3)                                         |
| Overtime policy                                                                     | Decision | **Pending** (PRD Q3)                                         |
| Headcount range (HRD Corp applicability)                                            | Decision | Pending (PRD Q2)                                             |

---

## Risks

| Risk                                                                        | Likelihood | Impact | Mitigation                                                                                                                                      |
| --------------------------------------------------------------------------- | ---------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| A statutory rate or rounding rule is wrong and staff are under or over paid | Med        | High   | Rates are data with `Verified` flags; approval blocked on unverified tables; rules as tests first; 3-month parallel run before go-live          |
| Salary data leaks (API, logs, exports, audit rows)                          | Med        | High   | Encrypted at rest; masked by permission; no salary in logs/audit `changes`; BOLA tests on every payslip route; exports require `payroll.export` |
| Two people edit one run                                                     | Med        | Med    | Version token on the run, stale save refused, Serializable on lifecycle transitions                                                             |
| Locked run is mutated by a later change (leave edited after lock)           | Med        | High   | Locked runs immutable; changes become adjustment lines in the next open run (tested)                                                            |
| Scope balloons (PCB, EA form, overtime)                                     | High       | Med    | PCB pluggable, EA form and automatic overtime explicitly deferred                                                                               |
| Removing `Role.HourlyRate` breaks the roles page and E2E                    | Med        | Low    | Done first in 8a with its own tests                                                                                                             |
| Floating-point creeps in (PDF, JSON, JS)                                    | Low        | High   | Decimal in C#; money serialised as strings or integer cents in the API; frontend never does arithmetic on money                                 |

---

## Implementation steps (each sub-phase: tests first, then code)

1. Write the 8a to 8f branches in order; each step above is one PR with its own tests. `[TDD]` on 8a, 8b, 8c, 8d.
2. Before 8b merges: confirm the seeded rates and the divisor with the user's accountant, or leave `Verified = false` and keep approval blocked.
3. Before payroll go-live (outside the PRs): DPIA (PRD PRV-14), accountant sign-off, 3-month parallel run against a manual calculation for 20 sample staff.

---

## Validation criteria

- [ ] Every PAY requirement P0 and P1 (PAY-01 to PAY-09) maps to a test (traceability table kept in the 8f PR)
- [ ] `dotnet test` green incl. Integration (run transitions under Serializable, stale token, BOLA on payslips)
- [ ] `npx ng test` green for all three projects; both apps build; ESLint, Prettier ratchet, `dotnet format` clean
- [ ] E2E green against rebuilt Docker (full run lifecycle, staff payslip, reopen refused after export)
- [ ] A hand-calculated payroll for at least five profiles (joiner, leaver, unpaid leave, above and below the EPF and SOCSO bands, advance repayment) matches the calculator to the sen
- [ ] No salary or bank value appears in logs, audit rows, error bodies or exports without `payroll.export`
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge of the sub-phase PR. Sub-phases are independent after 8a.
- **Data:** 8a's `DropRolePayFields` drops stored role rates (no consumer, no production data). Salary, run and payslip tables are additive; their `Down` drops them.

---

## Open questions

1. Overtime policy and the bank payment file format (PRD Q3): who answers, and when? (Assumed: later; manual overtime entry and a generic CSV ship first.)
2. Is the PRD's EPF/SOCSO/EIS rate set (researched 13 Nov 2026, UNCONFIRMED) the right starting point, and who verifies it? (Assumed: the accountant before any real run.)
3. Daily-rate divisor 26 and the 50% section 24 cap: confirm or replace. (Assumed 26 and 50%, both printed or enforced and editable as policy data.)
4. Headcount: 10 or more Malaysian employees makes the 1% HRD Corp levy compulsory. (Assumed informational only.)
5. Are public holidays paid and excluded from the unpaid-leave count? (Assumed no calendar in v1.)

---

## Review Notes

_Filled in after each sub-phase._

| Issue | Perspective | Severity | Affects Phase | Resolution |
| ----- | ----------- | -------- | ------------- | ---------- |

## Sign-off

- [ ] All sub-phases (8a to 8f) merged
- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] ADR written; CLAUDE.md and README updated
- [ ] Go-live gate (accountant sign-off, parallel run) recorded outside the repo
