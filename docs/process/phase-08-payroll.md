# Phase 8: Payroll rebuild (monthly salary, statutory deductions, payslips)

> **Status:** `8a merged (#103); 8b implemented (calculator, rate tables, verification, rates page); 8c to 8f still summaries`
> **Created:** 2026-11-13 | **Updated:** 2026-11-14
> **Depends on:** Phases 7, 7j, 7i, 7l merged (all done). **All payroll screens (including the staff payslip page) are built in the single web app of Phase 7n**, not in separate admin and staff apps. **8b onward also depends on Phase 7m (company policy)** (`phase-07m-company-policy.md`): the working week, hours per day and the unpaid-leave divisor are company settings, not constants. 8a does not. Source of requirements: `HRNexus PRD v1.0`, section 5.4 (PAY-01 to PAY-10), section 6.2 (calculation order and run lifecycle), LEV-11, STF-05.
> **Environment note (13 Nov 2026):** no production deployment and no real data, so drop migrations and rate-table seeding need no backup yet. **Payroll go-live (not this phase's merge) is gated** by an accountant sign-off and a 3-month parallel run (PRD).

This phase is too large for one pass (Rule 13). It is delivered as **six sub-phases (8a to 8f)**, each one PR, each independently green. A later sub-phase starts only after the earlier one merged.

---

## Objectives

1. Pay is **monthly salary**, stored per person as an effective-dated history. Hourly pay is gone from the product (PRD non-goal).
2. A **payroll run** per month (draft, reviewed, approved, locked) calculates every payslip line by line in the PRD's fixed order, with exact decimals, and a locked run never changes.
3. EPF, SOCSO and EIS are computed from **rate tables stored as data with an effective date**, never hard-coded. **PCB (monthly tax) is entered by hand per person per month in v1** (owner decision, 14 Nov 2026); calculating it is deferred.
4. Staff read **only their own payslip** (PDF) in the staff app; salary is masked everywhere without a payroll permission.
5. Payroll locks and cash advances are folded into the run; unpaid leave and no-show flow in from the leave module.
6. Every figure on a payslip can be explained, and the rules exist as tests before the code (PRD risk control: "Payroll bug underpays staff").

---

## What exists today (verified on `master` at `e77ed4f`)

| Area                                                                         | State                                                                                                                                                                                                                                                           |
| ---------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Role.HourlyRate`, `Role.PayType`, `RoleRate` (rate history)                 | Left from the shift-log era; **no consumer**. Hourly pay is a non-goal, so they are removed in 8a                                                                                                                                                               |
| `PayrollLock` (+ `PayrollLockGuard`, controller, admin page, `payroll.lock`) | Month locks with no payroll behind them; folded into the run in 8f                                                                                                                                                                                              |
| `CashAdvance` (+ commands, admin page, `cashadvance.manage`)                 | Records and settles advances; becomes a deduction source in 8c                                                                                                                                                                                                  |
| Leave                                                                        | Stored in **hours** (Mon-Fri, 8h/day, Phase 7l); leave whose type is flagged `DeductsPay` in the company's leave-type catalogue (Phase 7q; `unpaid` and `no-show` by default) is what reduces pay. The PRD stores days: payroll converts `hours / WorkdayHours` |
| Permissions                                                                  | `payroll.view`, `payroll.manage`, `payroll.lock`, `cashadvance.manage` exist as catalog keys; no payroll behaviour behind them                                                                                                                                  |
| Encryption                                                                   | `IFieldEncryptor` (AES-256-GCM) protects Staff PII; salary and bank details join the encrypted set                                                                                                                                                              |

---

## Decisions made in this plan (change them here if wrong)

1. **Monthly salary only.** Per-person `SalaryProfile` (effective-dated rows). A month is built from **salary segments**: each profile applies from its `EffectiveFrom` day (not from the first of the month), so a raise on the 15th changes pay from the 15th and a joiner whose first profile starts after the employment start has the uncovered days flagged. Pay frequency is stored (default monthly) but only monthly runs exist in v1.
2. **Money is `decimal` (MYR) end to end**, with the rounding rule of each scheme declared **in its rate table** (never in code, and not asserted here: the PRD only says "the rounding rule of each scheme", so each rule is confirmed by the accountant). No floating point anywhere in the calculator. The API returns money as JSON numbers (amounts with at most 2 decimals, rates with at most 4), as 8a already does; the frontend only displays and sends money and never does arithmetic on it.
3. **Rate tables are data.** `StatutoryRateTable` rows (scheme, effective-from, wage base rule, ceiling, employee rate, employer rate, band table where the scheme uses one). Seeded from the PRD figures but every row carries `Verified = false` until the accountant confirms; a payroll cannot be **approved** while any table it used is unverified (guard in 8b/8c).
4. **Calculation order is fixed** (PRD 6.2): prorated basic, unpaid-leave deduction, statutory, other deductions, rounding. Each step takes only earlier output so a payslip explains itself.
5. **Unpaid-leave deduction** = monthly basic / **divisor** x unpaid working days, capped at the prorated basic. Divisor is a company policy value, default **26** (UNCONFIRMED; printed on the payslip).
6. **Run lifecycle** exactly as the PRD table (start, edit/recalculate, submit, approve by a _different_ person, return, lock, reopen only before any bank or statutory file was exported, corrections go to the next run). Version token on the run; a stale save is refused with an error code.
7. **Single-person exception:** if only one user holds payroll permission the owner approves after signing in again; the run and payslip register are flagged "single-person approval" (PRD).
8. **Permissions:** `payroll.view` (see runs and salary), `payroll.manage` (prepare, edit, submit, lock), `payroll.approve` (new; approve and return), `payroll.reopen` (new; separate by PRD), `payroll.export` (new). Staff see only their own payslips through `/api/me/payslips` (authentication only, filtered by caller id).
9. **Overtime and other pay are hand-entered with a reason** (PAY-04); no automatic overtime in v1. A per-person overtime-rule field is deferred until the owner answers PRD question 3.
10. **No electronic filing.** v1 produces the bank file and statutory reports; a person submits them (PRD non-goal).
11. **Leavers and joiners** are prorated by calendar days; a deactivated person stays in that month's run until final pay is locked.
12. **Owner decisions of 14 Nov 2026 (8b):** the company has **10 or more Malaysian employees**, so the HRD Corp levy at 1% is on (still unverified, like every rate); **PCB is entered by hand** per person per month; a salary below the minimum wage is a **warning, not a block**; the first bank payment file is a **generic CSV**.

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
- Pure `PayrollCalculator` (no database): inputs are the person's month facts, outputs are payslip lines. **PCB is a hand-entered amount per person per month** (owner decision, 14 Nov 2026), applied as given; a system-calculated PCB is deferred.
- Tests first from hand-worked examples (EPF at and across RM5,000, SOCSO at the RM6,000 ceiling, proration for a mid-month joiner and leaver, unpaid-leave cap, deduction cap and carry-over) written with the accountant's sign-off in mind.

### 8c: Payroll run and inputs (about 2 days)

- `PayrollRun` (month, state, version token, preparer, approver, single-person flag, export status) and `PayrollRunLine` (one per person, with typed lines: basic, allowance, overtime, unpaid-leave deduction, EPF/SOCSO/EIS/PCB employee and employer, cash-advance repayment, adjustment).
- Allowances and deductions (PAY-02) as `PayItem` rows (fixed or one-off, per person), including cash-advance repayment schedule; manual overtime/other pay and the hand-entered PCB, each with a reason.
- Leave inputs (LEV-11): approved leave of every type flagged `DeductsPay` in the leave-type catalogue (Phase 7q: `unpaid` and `no-show` by default; payroll never names a type) in the month, converted from hours to days. Leave counted in calendar days (maternity, paternity) converts with the same hours-per-day; a change touching a locked month creates an **adjustment line** in the next open run.
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

## 8b detailed plan: rate tables and the calculator

> Reviewed by the opus reviewer on 14 Nov 2026 against the PRD and the 8a code; the confirmed findings are folded in below.

**What the PRD gives and does not give (checked on 14 Nov 2026).** PRD 6.2 gives the calculation order, the formulas, and one rate row per scheme: EPF 11% employee, 13% employer up to RM5,000 of wages and 12% above (citizens and permanent residents only); SOCSO category 1 0.5% / 1.75% and EIS 0.2% / 0.2%, both with a RM6,000 wage ceiling since 1 Oct 2024; HRD Corp 1% employer (compulsory with 10 or more Malaysian employees, optional 0.5% for 5 to 9); PCB "per LHDN schedule"; minimum wage RM1,700 from 1 Feb 2025. **It does not give** the EPF wage bands or rounding rule, any SOCSO or EIS band table or rounding, any PCB formula or schedule, which earnings belong in each scheme's wage base, payslip fields, or any worked example. So a calculator "right to the cent" cannot be proven from the PRD alone, and this plan does not pretend it can.

**The design that follows: separate the calculator from the tables.**

- The **calculator** is pure code that applies whatever rate tables it is handed, in the PRD's fixed order, with exact decimals. Its correctness (proration, segments, caps, order, rounding applied as declared, invariants) is provable now with tests.
- The **rate tables** are data. Whether a row matches the law is a different question only an accountant can answer, so every seeded row carries `Verified = false` and an unverified table blocks **approval** of a run (the guard is built here, used by 8c).
- A test therefore says "the calculator implements the rule declared in the table", never "this is the law".
- **The percentage seeds are placeholders, not a usable default.** The real EPF (Third Schedule), SOCSO and EIS schedules are believed to be **band tables** (EPF in steps of about RM20 up to RM5,000, contribution taken at the band's upper limit; SOCSO and EIS a fixed amount per wage band). A percentage of the exact wage will be wrong by some sen for most wages. The first question to the accountant is "band amounts or percentage?" (question 1 below); the band-table mechanism is built in 8b so the answer is a data load.

### Data model (migration `StatutoryRates`)

- **`StatutoryRateTable`**: `Id`, `Scheme` (`epf`, `socso`, `eis`, `hrd-compulsory`, `hrd-optional`, `minimum-wage`), `CitizenshipClass` (`citizen` or `permanent-resident`; **there is no "all" class**: a scheme with one rate for both has one row per class, so lookup is never ambiguous), `AgeFrom` (default 0) and `AgeTo` (nullable) on the person's age at month end (the over-60 rates are unconfirmed, PRD 6.2/6.6; until a row covers a person's age the calculator raises a blocking warning), `EffectiveFrom`, `Kind` (`percentage` or `band-table`), `WageCeiling` (nullable), `EmployeeRate`, `EmployerRate`, `EmployerRateMode` (`flat`, or `whole-wage-by-threshold` for EPF: the employer rate is chosen by whether the wage base is above `EmployerRateThreshold`, **then applied to the whole wage base** (assumed, not marginal)), `EmployerRateThreshold`, `EmployerRateAbove`, `Rounding` (`none`, `nearest-cent`, `nearest-5-sen`, `up-to-next-ringgit`; midpoint away from zero), `Amount` (for `minimum-wage`), `Active`, `Verified`, `VerifiedBy`, `VerifiedAt`, `Source`, `Note`. Unique on (`Scheme`, `CitizenshipClass`, `AgeFrom`, `EffectiveFrom`).
- **`StatutoryRateBand`** (child, `band-table` only): `WageFrom`, `WageTo` (inclusive from, exclusive to; a wage above the top band uses the top band, and a gap is a blocking warning), `EmployeeAmount`, `EmployerAmount`. When `WageCeiling` is set it is applied to the wage first, then the band is looked up.
- **Rows are immutable except `Active`, `Verified`, `VerifiedBy`, `VerifiedAt` and `Note`.** Rates, rounding, ceiling and **bands never change after insert**: a new rate (or a corrected band table) is a **new row** with a later `EffectiveFrom`, which starts unverified. So a verified row can never silently change, and any past month can be recalculated exactly.
- **How rows get in (8b):** the seed (insert-if-missing at startup, never `HasData`, so a runtime `Verified` flip cannot drift from the model snapshot) and a developer migration or script when the accountant supplies official tables. An admin screen for adding rows is deferred to 8f; 8b's page is read-only apart from "verify" and "active".
- **Lookup:** for a scheme, class and age, the row with the latest `EffectiveFrom` on or before the **first day of the month**. If a **newer row starts inside the month**, the calculator raises a blocking warning ("rate change inside the month, not supported") instead of guessing a split. A missing row for a scheme the person needs is a blocking warning, never a silent zero.
- Money is `decimal`: `numeric(18,4)` for rates, `numeric(12,2)` for amounts.

### Seed rows (all `Verified = false`, source "PRD 6.2 (13 Nov 2026)")

| Scheme               | Classes                                    | Kind       | Employee | Employer                                                                | Ceiling | Rounding (ASSUMED)                                                                 |
| -------------------- | ------------------------------------------ | ---------- | -------- | ----------------------------------------------------------------------- | ------- | ---------------------------------------------------------------------------------- |
| `epf`                | citizen, permanent-resident (one row each) | percentage | 11%      | 13% if the wage base is up to RM5,000, else 12%, on the whole wage base | none    | up to next ringgit                                                                 |
| `socso` (category 1) | both                                       | percentage | 0.5%     | 1.75%                                                                   | RM6,000 | nearest 5 sen                                                                      |
| `eis`                | both                                       | percentage | 0.2%     | 0.2%                                                                    | RM6,000 | nearest 5 sen                                                                      |
| `hrd-compulsory`     | both                                       | percentage | none     | 1%                                                                      | none    | nearest cent; `Active = true` (owner: 10 or more Malaysian employees, 14 Nov 2026) |
| `hrd-optional`       | both                                       | percentage | none     | 0.5%                                                                    | none    | nearest cent; `Active = false`                                                     |
| `minimum-wage`       | both                                       | amount     | n/a      | RM1,700 from 2025-02-01                                                 | n/a     | n/a                                                                                |

At most one `hrd-*` row may be active. The rounding column is **my assumption, not the PRD's**. The over-60 rates are not seeded, so a person over 60 gets the blocking warning until the accountant supplies them.

### The calculator (pure, no database; `PayrollCalculator` in Application)

**Input** (one person, one month):

- the month and its days; the person's **age at month end** (computed by the caller from the date of birth; the calculator never uses `Staff.Age`, which uses the clock and a 365.25-day year) and citizenship class;
- **salary segments**: `(from, to, basic)` built by the caller from the effective-dated profiles clipped to the employment start and end dates inside the month; days of employment not covered by any profile are returned as a blocking warning;
- **unpaid leave**: a list of `(date, days)` with days as an exact decimal fraction (for example 4 hours with an 8-hour day is 0.5; 3 hours is 0.375), already converted by 8c from the hours leave stores, using the policy's hours per day; and the divisor (company policy, default 26);
- **earnings** (allowances, overtime, other earnings), each with a reason and two flags saying whether it counts in the **EPF** and in the **SOCSO and EIS** wage base (basic and the unpaid-leave deduction always count; the defaults per pay-item type are data from 8c, UNCONFIRMED: EPF is believed to exclude some items that SOCSO includes);
- **other deductions** (each with a priority), the **deduction cap rate** (a parameter; stored in company policy by 8c; default 50% of gross, BELIEVED, applies to other deductions only as PRD 6.2 says, not to the statutory employee contributions);
- the resolved **rate tables** and the person's hand-entered **PCB** amount (with a reason; 0 when none is entered).

**Order, exactly as PRD 6.2** (each step uses only earlier output, so every line can be explained):

1. **Prorated basic**, per salary segment: basic x calendar days in the segment / days in the month; a full month with one segment is not prorated. Joiner, leaver, joined-and-left and a mid-month raise are all just segments. Rounded to the cent at the end of the step.
2. **Unpaid-leave deduction**: for each unpaid day, the basic **of the segment that day falls in** / divisor x days; the total is **never more than the prorated basic**. Rounded to the cent.
3. **Gross** = prorated basic + allowances + overtime + other earnings - unpaid-leave deduction (PRD formula).
4. **Statutory** per scheme, on that scheme's wage base (the sum of the earning lines whose flag is set, plus prorated basic, minus the unpaid-leave deduction), the ceiling applied first, then the rate or the band, then the scheme's rounding; employee and employer amounts are rounded separately. EPF's employer rate follows `EmployerRateMode`. The levy is added only if an `hrd-*` row is active.
5. **PCB** is the hand-entered amount, applied as given and never above what remains of net pay. There is no schedule, no formula and no tax profile in v1. If none is entered it is 0 with an **informational** note "no PCB entered" (a person below the tax threshold legitimately has none).
6. **Other deductions** last, in priority order, each limited so that their total never exceeds the cap share of gross and **net pay never goes below zero**; whatever cannot be applied is returned as **unapplied** (8c carries it to the next run).
7. **Net** = gross - employee EPF, SOCSO, EIS - PCB - applied other deductions. **Employer cost** = gross + employer EPF, SOCSO, EIS + HRD levy (PRD formula).

**Output:** typed lines (`basic`, `allowance`, `overtime`, `other-earning`, `unpaid-leave`, `epf-employee`, `epf-employer`, ..., `pcb`, `deduction`), gross, net, employer cost, the unapplied deductions, and **everything needed to reproduce the figures months later** (the divisor, the **ids of the rate rows used**, the salary profile ids and segments, the age class, and a calculator version constant; 8c adds the policy version). Plus **warnings**, each `blocking` or `informational`: no PCB entered (informational), an unverified table used, no rate row for a needed scheme or age, a rate change inside the month, no citizenship class, employment days with no salary, a band gap, a leaver whose end date is before the month, salary below the minimum-wage row (informational: question 6), unapplied deductions.

**Rules that are not negotiable:** no floating point anywhere; rounding happens only where a table says so; the calculator never reads the clock or the database; the same input always gives the same output.

### Verification guard, permission and API (built and tested here, used by 8c)

- `StatutoryRates.UnverifiedAsync(month, persons)` returns the tables a calculation would use that are not verified; 8c refuses **approve** while it is non-empty.
- **Separation of duties:** marking rates verified needs a **new permission `payroll.rates.verify`** (catalog key, label, description and the frontend `PermissionKey` union; the first administrator role gets it automatically). The preparer's `payroll.manage` is not enough, so the person who prepares payroll cannot also verify the rates that gate its approval.
- `GET /api/payroll/rates` (`payroll.view`) lists tables with bands; `POST /api/payroll/rates/{id}/verify` and `.../active` (`payroll.rates.verify`; a note is required). The verify handler changes the entity and then calls `LogAsync` **once** so both commit in one save (the audit service saves by itself); `VerifiedBy` comes from the current user, never from the request. The audit row carries no amounts.
- A small read-only **Statutory rates** page (under payroll, `payroll.view`) shows tables, bands and a Verified badge, with "Mark verified" for `payroll.rates.verify` holders.
- **8a fix folded in:** `AddSalaryProfileCommand` never passes `createdBy`, so `SalaryProfile.CreatedBy` is always null; 8c's "preparer is not the approver" needs it. Fix and test it here.

### Accountant review pack (a deliverable)

`tests/fixtures/payroll-calculator.json` holds named worked examples (inputs, rate rows, expected lines). **The expected numbers are worked out by hand, with the formula shown beside each figure in the pack, and are not generated by running the calculator**, so the calculator is checked against an independent calculation. A small script (culture-invariant formatting) renders the fixture to `docs/process/payroll-worked-examples.md` so the accountant can read each example as a table and sign it off or correct it. Corrections change the fixture and the table data; the tests then prove the calculator still follows them.

### Steps (tests first; about 2.5 days)

1. `[TDD]` **Money and rounding helper** (`MoneyRounding`): the four rules, midpoint away from zero, exactness, invariant culture. (~1 hour)
2. `[TDD]` **Rate tables:** entities, migration, insert-if-missing seed, immutability of rates and bands, effective-date lookup (boundary on the first day; a row starting inside the month; age rows; no overlap), missing-row and band-gap warnings, uniqueness. Integration test on real Postgres for the migration, the unique index and the seed. (~4 hours)
3. `[TDD]` **Calculator, step by step** from the hand-worked vectors, in PRD order: segments (full month, joiner, leaver, joined and left, mid-month raise, leap-year February, 28/30/31-day months, uncovered days), unpaid-leave deduction (normal, fractional days, capped at prorated basic, divisor from policy, basic of the segment the day falls in), gross, EPF (employer rate at RM5,000.00 and RM5,000.01 on the whole wage base; rounding up; base flags excluding an item), SOCSO and EIS (below, at and across the RM6,000 ceiling), HRD levy active (1%, owner: 10 or more employees), off, and the optional 0.5% row, band-table lookup (inclusive and exclusive edges, gap, above the top band, ceiling then band), PCB entered and not entered, other deductions (cap, priority, unapplied, net never negative). (~6 hours)
4. `[TDD]` **Invariants** over generated inputs: net and employer cost equal their formulas exactly; no earning or statutory line is negative (adjustment lines of 8c may be); a ceiling never increases a contribution; the output is identical across repeated calls and across cultures; money survives a JSON round trip. These guard structure; the hand-worked vectors are the oracle for the figures. (~1.5 hours)
5. **Verification guard, permission, API, page** (403 without the permission, the preparer cannot verify, audit without amounts, atomic save), the `CreatedBy` fix, and the read-only page and its spec. (~4 hours)
6. **Review pack renderer** and the first pack; CLAUDE.md "Payroll rules"; this plan's status. (~1.5 hours)

**Done when:** every vector passes, the invariants hold, the migration and seed run on a fresh and on an existing database, an unverified table is reported by the guard, verify needs `payroll.rates.verify`, the rates page works for `payroll.view`, and the review pack is in `docs/process/`. **Not done by 8b:** approving a real run, loading official band tables or calculating PCB, anything per person (8c).

**Risks specific to 8b**

| Risk                                                                     | Mitigation                                                                                                                                                                       |
| ------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A seeded rate or rounding assumption is wrong and looks authoritative    | `Verified = false` everywhere, "ASSUMED" in the seed notes, the percentage seeds called placeholders, approval blocked, the review pack, the 3-month parallel run before go-live |
| The wage base differs by scheme and item                                 | Two flags per earning line (EPF; SOCSO and EIS), covered by a test per rule; the accountant's answer changes data, not code                                                      |
| Hand-entered PCB is wrong or forgotten                                   | The amount is a visible input with a reason and an informational "no PCB entered" note; the accountant supplies it; calculating PCB (tax profile, schedule) is a later phase     |
| Money drifts through a rounding step applied twice or at the wrong point | Rounding only inside the step that declares it; invariants test exact equality; hand-worked vectors are the oracle; no float types in the calculator                             |
| A verified row is edited and the verification goes stale                 | Rates, ceiling and bands are immutable; a correction is a new unverified row                                                                                                     |
| The preparer verifies the rates that gate their own approval             | Separate `payroll.rates.verify` permission                                                                                                                                       |
| A month cannot be reproduced later                                       | Output records rate row ids, profile ids, segments, age class and a calculator version                                                                                           |

**Model split:** implementation on sonnet; an opus review of the finished PR because it fixes the money contract and the data shape that 8c to 8f build on.

### Questions for the accountant (the defaults are what the code starts with)

**Answered by the owner on 14 Nov 2026:** HRD Corp levy on at 1% (10 or more employees); PCB entered by hand; salary below the minimum wage warns but does not block; the first bank file is a generic CSV.

1. **EPF, SOCSO, EIS:** the official band amounts (EPF Third Schedule, PERKESO tables) or a percentage with rounding (the assumed placeholder)? The wage bands? The rounding rule? Over-60 rates and age-based exemptions (PRD 6.6)?
2. **EPF employer rate:** 13% or 12% decided by the wage base against RM5,000 and applied to the **whole** wage (assumed), or marginal?
3. **Wage base:** which earnings count in the EPF and which in the SOCSO and EIS (assumed: all of them, so the base is gross after the unpaid-leave deduction)?
4. **Section 24:** is the cap on other deductions 50% of wages (believed), of which base, and may fines be deducted at all?
5. **Minimum wage later:** a rise to RM2,000 from June 2027 is only a press report; add it as a `minimum-wage` row when confirmed.

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
| LHDN PCB schedule as data (only when PCB calculation is built)                      | External | Deferred                                                     |
| Company bank payment file layout                                                    | External | **Pending** (PRD Q3)                                         |
| Overtime policy                                                                     | Decision | **Pending** (PRD Q3)                                         |
| Headcount range (HRD Corp applicability)                                            | Decision | Pending (PRD Q2)                                             |

---

## Risks

| Risk                                                                        | Likelihood | Impact | Mitigation                                                                                                                                           |
| --------------------------------------------------------------------------- | ---------- | ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| A statutory rate or rounding rule is wrong and staff are under or over paid | Med        | High   | Rates are data with `Verified` flags; approval blocked on unverified tables; rules as tests first; 3-month parallel run before go-live               |
| Salary data leaks (API, logs, exports, audit rows)                          | Med        | High   | Encrypted at rest; masked by permission; no salary in logs/audit `changes`; BOLA tests on every payslip route; exports require `payroll.export`      |
| Two people edit one run                                                     | Med        | Med    | Version token on the run, stale save refused, Serializable on lifecycle transitions                                                                  |
| Locked run is mutated by a later change (leave edited after lock)           | Med        | High   | Locked runs immutable; changes become adjustment lines in the next open run (tested)                                                                 |
| Scope balloons (EA form, overtime, PCB calculation)                         | High       | Med    | PCB is hand-entered in v1; EA form and automatic overtime explicitly deferred                                                                        |
| Removing `Role.HourlyRate` breaks the roles page and E2E                    | Med        | Low    | Done first in 8a with its own tests                                                                                                                  |
| Floating-point creeps in (PDF, JSON, JS)                                    | Low        | High   | Decimal in C#; money is a JSON number with at most 2 decimals (as in 8a) and the frontend only displays it, never computes with it; round-trip tests |

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
