# Phase 7q: Leave types as company data (annual, sick, hospitalisation, maternity, paternity, compassionate)

> **Status:** `Draft` (plan reviewed by the opus reviewer on 14 Nov 2026 against the code; findings folded in below)
> **Created:** 2026-11-14 | **Updated:** 2026-11-14
> **Depends on:** Phases 7l and 7m merged (leave rules, company policy). Independent of Phase 8 apart from one rule: **payroll deducts pay only for leave whose type says `DeductsPay`** (see "Payroll"), so this phase should land before 8c.
> **Why:** the owner asked (14 Nov 2026) for more leave types: **annual, sick, hospitalisation, maternity, paternity and compassionate**. Today the product has five hard-coded types (`annual`, `sick`, `emergency`, `unpaid`, `no-show`), and every rule that differs between them (balances, auto-approval, advance notice, which pool is charged) is a name comparison in about 15 places. Adding four more the same way would copy that again, and the next company will want different ones. Phase 7m's rule applies: rules that differ between companies are settings, not constants.

---

## Objectives

1. A company's **leave types are data** (a catalogue), editable by someone with `policy.manage` on the company policy page. Each type says how it behaves.
2. The owner's types exist from day one: annual, sick, **hospitalisation, maternity, paternity, compassionate**, plus the existing emergency, unpaid and no-show, so nothing that works today stops working.
3. **No behaviour change for the five existing types** on an untouched installation, proven by the existing leave test suite passing **unchanged** against the default catalogue.
4. Staff apply for any **active** type from one list; managers see the same list; balances show each type's allowance and what is left.
5. Health and family facts are not broadcast: a type can be marked **sensitive**, and then notifications and push messages say "leave request", not the type.
6. Payroll learns from the catalogue which leave **deducts pay**, so it never names a leave type.

---

## What is hard-coded today (verified on `master`; the reviewer found the extra rows)

| Rule                                                                                                                                                                                                                                                                                                                                                                                      | Where                                                                                                                                                                                                                                                         | Becomes                                                                                                       |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| The five type names and the list that validates them                                                                                                                                                                                                                                                                                                                                      | `AppConstants.Leave`, `UpsertLeaveCommand.ValidateRequest`, `leave.component.ts` (`LEAVE_TYPES`, labels at lines 113-117, 350, 474, 540, 743, 776), `my-leave.component.ts` (`LEAVE_TYPES`, 44, 138-140), `leave.model.ts`, the tags in both pages' templates | the catalogue                                                                                                 |
| Sick and emergency leave are auto-approved when applied for                                                                                                                                                                                                                                                                                                                               | `UpsertLeaveCommand` (`isSick`, `isEmergency`, lines 128-131)                                                                                                                                                                                                 | `AutoApprove` flag, **only for types that have a balance**                                                    |
| Annual leave needs notice. The **server** uses `policy.AnnualLeaveAdvanceDays`; **`my-leave.component.ts:76-83` hard-codes 3 days**                                                                                                                                                                                                                                                       | both                                                                                                                                                                                                                                                          | `AdvanceNoticeDays` on the type (annual = the policy value); fixes the frontend bug                           |
| **A request longer than `CompanyPolicy.MaxLeaveDays` (30 calendar days) is refused before any type rule** (`UpsertLeaveCommand.cs:225`)                                                                                                                                                                                                                                                   | `ValidateRequest`                                                                                                                                                                                                                                             | a type's `MaxDaysPerRequest` **replaces** the policy cap for that type; types without one keep the policy cap |
| Two balance pools: annual (which **includes emergency**) and sick, per-person yearly entitlements in `LeaveEntitlement`                                                                                                                                                                                                                                                                   | `EnsureSufficientBalanceAsync`, `GetLeaveBalanceQuery`, `GetAllLeaveBalancesQuery`, `GetLeaveSummaryQuery`, `CarryOverLeaveEntitlementCommand`                                                                                                                | `Pool` on a type; annual and sick keep their per-person entitlements                                          |
| `no-show`: admin-only, cannot be applied for; ended leave can only be retyped to it                                                                                                                                                                                                                                                                                                       | `LeaveController:32-33`, `BulkMarkLeaveNoShowCommand:36,39`, `UpsertLeaveCommand:244` (`becomesNoShow`)                                                                                                                                                       | a **system type** (see decision 4)                                                                            |
| Sick-leave pattern warnings                                                                                                                                                                                                                                                                                                                                                               | `GetLeavePatternWarningsQuery`                                                                                                                                                                                                                                | stay keyed to the `sick` system type                                                                          |
| Hours of a multi-day leave across New Year are **recomputed from the schedule passed in and ignore the stored hours**                                                                                                                                                                                                                                                                     | `LeaveHoursCalculator.HoursInYear`, called with `policy.Schedule` from `UpsertLeaveCommand:274,301`, `GetLeaveBalanceQuery:51`, `GetLeaveSummaryQuery:54`, `CarryOverLeaveEntitlementCommand:65`                                                              | every caller passes the **type's** schedule (all seven days for calendar-day types)                           |
| The leave type key appears in messages that reach other people: the approver notification and SignalR payload (`UpsertLeaveCommand:169`), fixed sick and emergency titles (`:160-162`), the employee's push body (`:361,373`), the withdrawal message (`CancelLeaveCommand:80`), the auto-reject job (`LeaveJobs.cs:114`), audit rows (`UpsertLeaveCommand:151`, `CancelLeaveCommand:31`) | those files                                                                                                                                                                                                                                                   | the `Sensitive` flag (see "Privacy")                                                                          |
| `LeaveBalanceDto` and `LeaveSummaryDto` have fixed fields per type, used by `home.component.html:30-61`, `leave.component.html` (25 matches), `my-leave.component.html` and `leave.model.ts`                                                                                                                                                                                              | queries and pages                                                                                                                                                                                                                                             | keep the old fields, **add a `pools[]` array**                                                                |
| Keys are at most 20 characters (`LeaveRequestConfiguration.cs:12`)                                                                                                                                                                                                                                                                                                                        | database                                                                                                                                                                                                                                                      | the key rule below                                                                                            |
| About 154 type-name uses in 15 backend test files (including `ConcurrencyTests` and `LoadConcurrencyTests`), 6 E2E specs, and `docs/process/phase-08-payroll.md` lines 30 and 73                                                                                                                                                                                                          | tests, docs                                                                                                                                                                                                                                                   | the parity run, then updated as needed                                                                        |
| Gender                                                                                                                                                                                                                                                                                                                                                                                    | not stored anywhere                                                                                                                                                                                                                                           | **not modelled**: see decision 6                                                                              |

---

## Design

### The catalogue

- **`LeaveTypeDefinition`** (one row per type) with: `Key` (immutable slug, lowercase letters, digits and hyphens, **at most 20 characters**, the value already stored on every `LeaveRequest`), `Label`, `Colour` (a small fixed set), `SortOrder`, `Active`, `System` (see decision 4) and:
  - **balance:** `Pool` (`annual`, `sick`, `own`, `none`). `annual` and `sick` use the existing per-person `LeaveEntitlement`. **`own`** has a company-wide yearly `AllowanceDays` (days x `CompanyPolicy.WorkdayHours`, the same for everyone; hours per day is a company setting, not per person). `none` has no balance. **Own pools do not carry over: they lapse each year.**
  - **limits:** `MaxDaysPerRequest` (nullable; **calendar days**, replaces the policy cap for this type), `AllowanceDays` (own pools).
  - **behaviour:** `AutoApprove` (only when `Pool` is not `none`: an auto-approved request skips the "not your own decision" check, which is acceptable for a balance-limited sick or emergency day and not for 98 days of maternity), `AdvanceNoticeDays` (nullable), `DeductsPay`, `AdminOnly`, `CountsCalendarDays` (consecutive calendar days instead of the company's working days), `Sensitive`.
  - a version token and audit fields, like `CompanyPolicy`.
- **Domain rules** (tested): keys never change; `Pool = own` needs `AllowanceDays`; `AdminOnly` types cannot be `AutoApprove`; `AutoApprove` needs a pool; at least one type is active; day limits are 0.5 to 366; **`Pool` and `CountsCalendarDays` cannot change once any leave uses the type** (changing them would refund or recharge past hours); a type with leave cannot be removed, only deactivated.
- **Defaults live in code, not only in the database.** `LeaveTypeCatalog` returns the **code defaults** (`LeaveTypeDefaults`) when no catalogue has been saved, exactly as `CompanyPolicyReader` does for the policy, and returns the saved rows once an administrator saves (the first save materialises all defaults). This is what lets every existing test, which never seeds a table, run unchanged. No migration data is needed beyond creating the table.
- **System types:** `no-show` and `sick` are **system types**. Their key, pool and flags are locked and they cannot be deactivated (no-show is how an administrator marks a leave that was not taken, and the sick-pattern warnings and the "retype ended leave" rule depend on them); an administrator may change their label and colour. Everything else is editable.
- **Active rules:** a type must be active to be **created** or to be a **retype target**; a status-only edit (approve, reject, cancel) of a leave whose type was later deactivated is always allowed, and retyping into an inactive or admin-only type is refused.

### Default catalogue (a new or untouched install; the owner's list plus today's types)

| Key                | Pool                                            | Limit / allowance                                                                   | Flags                          |
| ------------------ | ----------------------------------------------- | ----------------------------------------------------------------------------------- | ------------------------------ |
| `annual`           | `annual`                                        | per-person entitlement (today)                                                      | advance notice from the policy |
| `emergency`        | `annual` (charged to the annual pool, as today) | none                                                                                | auto-approve                   |
| `sick` (system)    | `sick`                                          | per-person entitlement (today)                                                      | auto-approve                   |
| `hospitalisation`  | `own`                                           | **60 days a year (ASSUMED)**                                                        | sensitive                      |
| `maternity`        | `none`                                          | **98 consecutive calendar days per request (ASSUMED minimum)**                      | calendar days, sensitive       |
| `paternity`        | `none`                                          | **7 consecutive calendar days per request (ASSUMED)**                               | calendar days, sensitive       |
| `compassionate`    | `none`                                          | **3 days per event, no yearly cap (ASSUMED: common practice, no statutory figure)** | sensitive                      |
| `unpaid`           | `none`                                          | none                                                                                | deducts pay                    |
| `no-show` (system) | `none`                                          | none                                                                                | deducts pay, admin only        |

The figures marked ASSUMED are general knowledge of the Employment Act and common practice, **not** confirmed for this company: counsel or the accountant confirms them, and the company changes them on the policy page (data, not code). The five existing types keep their keys and behave as today.

### Behaviour

- **Applying:** the type must be active and not `AdminOnly` for staff, within its day limit (or the policy cap), with its notice honoured. Hours are charged by the type's day counting: the company's working days, or **every calendar day** for calendar-day types (the existing `LeaveHoursCalculator` is called with an all-seven-days schedule).
- **Calendar-day leave across New Year:** because `HoursInYear` recomputes from the schedule it is given, **every caller** (apply, balance, summary, carry-over, the frontend preview) passes the type's schedule, and the shared fixture `tests/fixtures/leave-hours.json` gets calendar-day vectors asserted by both stacks (a 98-day maternity is 784 hours at 8 hours a day and must show as 784 in every view).
- **Balances:** `Pool = own` is charged per year like annual and sick. Per-event types (`none`) are limited only by their day limit and a manager's approval.
- **Changing a type is not retroactive for stored hours;** a changed allowance changes the remaining balance of the current year (it is a limit, not a stored figure).
- **Response shapes:** `LeaveBalanceDto` and `LeaveSummaryDto` keep their existing fields (so nothing else moves) and gain a `pools[]` array driven by the catalogue; the pages switch to it in 7q-c.

### Privacy: the `Sensitive` flag

A leave type reveals health and family facts. Today the raw key reaches every `leave.approve` holder in the notification, the SignalR payload, the push body on a lock screen, the withdrawal message and the auto-reject message. For a **sensitive** type those messages say "leave request" and carry no type or label; the approver still sees the type on the approval screen itself (they need it to decide). The audit trail keeps the key (audit is for administrators). New health and family types are sensitive by default. **`sick` is not sensitive by default** because today's titles ("Sick Leave Reported") are existing behaviour and parity comes first; an administrator can switch it on.

### Frontend

- A `LeaveTypeService` (shared-lib) loads the **active** types once per session, like the company policy; both leave pages build their type lists, tag colours, hints and notice rules from it (the hard-coded `LEAVE_TYPES`, label maps, colour ternaries and the `+ 3` days go).
- **Policy page:** a "Leave types" section: a table (label, key, balance, limit, flags, active) and an edit dialog with the same client checks as the server.
- **API:** `GET /api/leave-types` (anyone signed in; the active types, or all for `policy.manage`) and `PUT /api/leave-types` (`policy.manage`) that **replaces the whole catalogue with one version token**, like `PUT /api/company-policy` (add, edit and deactivate are all one save; a stale version is refused; a type with leave cannot be dropped from the list).

### Payroll

Phase 8c reads "leave that deducts pay" from the catalogue (`DeductsPay`) instead of naming types: today unpaid and no-show. Paid types never change the payslip. **Maternity and paternity pay depend on eligibility** (maternity: employment of at least 90 days in the 9 months before confinement and fewer than 5 surviving children; paternity: a married man employed at least 12 months, at most 5 confinements, notice about 30 days ahead; all PLAUSIBLE, counsel to confirm). The product does not model eligibility, so `DeductsPay` is per type and the default is paid; **if an employee is not eligible, the manager records the leave as `unpaid`**. The 8c plan line is updated in the same PR.

---

## Decisions made in this plan (change them here if wrong)

1. **One catalogue per installation**, edited by `policy.manage`; no per-department or per-person types.
2. **Emergency stays** (the owner's list did not mention it; removing it would change today's behaviour). It is a catalogue row an administrator can deactivate.
3. **Balances stay per-person for annual and sick** (so Phase 7l's entitlement and carry-over work unchanged); the new pooled type (hospitalisation) uses one company-wide allowance.
4. **`sick` and `no-show` are locked system types** (see Design).
5. **Maternity and paternity are per event with no yearly balance:** at most N consecutive calendar days per request plus a manager's approval. Maternity "98 days" is the **statutory minimum**, so a company that grants more raises the limit on the page. Confinement dates and counts are not modelled.
6. **No gender-based eligibility** and no eligibility rules at all: the product stores no gender, and adding it is a privacy decision of its own. The approver checks eligibility.
7. **Hospitalisation is its own pool, separate from sick leave.** The Employment Act is believed to count hospitalisation days together with the 14, 18 or 22 outpatient sick days toward one limit of 60, so a separate pool of 60 is **more generous than the statute** (legal, but a decision). A combined pool is a follow-up if counsel says so.
8. **Documents and certificate numbers wait for Phase 9.** `RequiresReference` is **cut from this phase** (a reference in the notes is free text, limited to 500 characters and overwritten by admin edits); when documents arrive, the reference becomes its own column.
9. **Public holidays and replacement leave** are out of scope (they need the company holiday calendar, deferred since 7l).

---

## Scope

### In scope

- Backend: `LeaveTypeDefinition`, `LeaveTypeDefaults`, `LeaveTypeCatalog`, every leave handler and query rewired off type names (the places above), the type-aware schedule, sensitive messages, `GET` and `PUT /api/leave-types`, migration (the table only), audit.
- Frontend: `LeaveTypeService`, both leave pages, the balance views and the home page driven by it, the policy page section, the `+ 3` fix.
- Tests: the existing leave suites unchanged and green on the defaults (parity), a test per flag and per new type, catalogue validation, fixture vectors for calendar-day leave in both stacks, E2E.
- Docs: CLAUDE.md "Leave rules", the 8c plan line, the Phase 8 doc lines 30 and 73, an ADR note.

### Out of scope (deferred)

- Eligibility rules and gender, confinement counting, certificate upload and a reference column (Phase 9), the combined sick and hospitalisation limit, public holidays, replacement leave, tenure bands for annual entitlement, effective-dated catalogue history, carry-over of own pools.

---

## Risks

| Risk                                                                                              | Likelihood | Impact | Mitigation                                                                                                                                                                         |
| ------------------------------------------------------------------------------------------------- | ---------- | ------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A missed hard-coded comparison keeps an old rule alive for new types                              | Med        | Med    | A guard test: no leave-type name comparison outside `LeaveTypeDefaults` and the two system types; every handler takes the type's definition                                        |
| The refactor changes behaviour for the five existing types                                        | Med        | High   | **7q-a0 changes nothing but where the rules come from**: the existing leave suites (about 154 type-name uses) run unchanged against the code defaults before anything new is added |
| Calendar-day leave is mis-split across New Year                                                   | Med        | Med    | Every `HoursInYear` caller passes the type's schedule; shared fixture vectors for both stacks                                                                                      |
| A catalogue edit strands or rewrites history                                                      | Low        | High   | Keys immutable; `Pool` and `CountsCalendarDays` locked once a type has leave; a type with leave is deactivated, never removed; status edits of old leave are always allowed        |
| A 98-day auto-approved leave bypasses "not your own decision"                                     | Low        | High   | `AutoApprove` only for types with a pool; maternity and paternity have none                                                                                                        |
| Health facts reach people who do not need them                                                    | Med        | High   | `Sensitive` flag on by default for the new types; messages and push bodies tested for a sensitive type                                                                             |
| The ASSUMED statutory figures or the maternity and paternity pay rules are wrong for this company | Med        | Med    | Marked in the defaults and on the page; counsel and the accountant confirm; data, not code                                                                                         |
| Response-shape change breaks the home page or leave views                                         | Med        | Med    | Old fields kept, `pools[]` added; pages move over in 7q-c                                                                                                                          |

---

## Implementation steps (about 4 to 5 days)

**7q-a0: read-only catalogue with parity (about 1 day).** `[TDD]` `LeaveTypeDefinition` aggregate (rules above) and `LeaveTypeDefaults`; `LeaveTypeCatalog` returning the code defaults; rewire `UpsertLeaveCommand`, the three balance queries, the summary, carry-over, the controllers and the type validation to read it; **no behaviour change; the existing leave suites pass unchanged**; add `pools[]` to the DTOs.

**7q-a1: behaviour and the new types (about 1.5 days).** `[TDD]` Test first for each flag (`AutoApprove` with a pool only, `AdvanceNoticeDays`, `AdminOnly`, `MaxDaysPerRequest` replacing the policy cap, `CountsCalendarDays`, `DeductsPay`, `Sensitive` messages and push), `Pool = own` across New Year, hospitalisation, maternity (98 calendar days across a weekend accepted and 99 refused, 784 hours in every view), paternity 7 and 8, compassionate, an inactive type refused for create and retype but allowed for approving old leave, the locked system types; calendar-day vectors in `tests/fixtures/leave-hours.json` asserted by xUnit and vitest.

**7q-b: persistence and API (about 1 day).** Migration (the table), `GET` and `PUT /api/leave-types` with the version token, audit, authorization, Integration tests including the first save materialising the defaults and a stale save refused.

**7q-c: frontend, E2E, docs (about 1.5 days).** `[TDD]` `LeaveTypeService`; both leave pages, the balance views and the home page driven by the catalogue; the `+ 3` fix; the policy-page "Leave types" section; E2E (an administrator adds a custom paid type and deactivates another, a staff member applies for it and a manager approves; an employee cannot apply for an admin-only type; the default types still work); CLAUDE.md, the 8c plan and Phase 8 doc lines, ADR note; multi-perspective review.

---

## Validation criteria

- [ ] No leave-type name comparison remains outside `LeaveTypeDefaults`, the migration and the two locked system types (guard test)
- [ ] The existing leave test suites pass **unchanged** on the default catalogue (the 7q-a0 gate)
- [ ] All nine default types exist; the four new ones can be applied for, approved, and show on balances and the calendar
- [ ] A maternity request of 98 calendar days across weekends is accepted and 99 is refused (and a 98-day request is **not** blocked by the 30-day policy cap); paternity 7 and 8 likewise; hours agree in every view
- [ ] Hospitalisation draws on its own 60-day allowance across New Year; compassionate is limited to 3 days per event
- [ ] An administrator can add, edit and deactivate a type; a type with leave cannot be removed; `Pool` and `CountsCalendarDays` are locked once used; staff only see active types
- [ ] A sensitive type never appears by name in a notification, push or withdrawal message
- [ ] `dotnet test` (all three projects), `npx ng test`, builds, ESLint, Prettier ratchet, `dotnet format` clean; E2E green against the rebuilt stack
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the sub-phase PRs in reverse order.
- **Data:** the migration adds one table; its `Down` drops it. Existing leave keeps its `LeaveType` key, so nothing stored changes shape, and with no catalogue saved the code defaults apply.

---

## Questions for the owner, counsel and the accountant (the defaults are what the code starts with)

1. **Statutory figures:** hospitalisation 60 days a year, maternity 98 consecutive days (and may a company grant more?), paternity 7 consecutive days, compassionate 3 days **per event**: right for this company?
2. **Sick and hospitalisation:** one combined limit (believed to be the statute) or separate pools (assumed)?
3. **Emergency:** keep it (assumed) or deactivate it for this company?
4. **Eligibility:** is a manager's check enough for maternity and paternity (assumed), and what proof is needed (marriage certificate, confinement date)?
5. **Pay:** for maternity and paternity, who is eligible for statutory pay, and if someone is not, is recording the leave as unpaid acceptable (assumed)?
6. **Accountant:** do EPF and SOCSO apply to maternity pay, and does unpaid leave counted in calendar days (including weekends) use the same divisor of 26 (`PayrollCalculator` deducts per day)?
7. **Annual entitlement by service:** does the company grant annual leave in tiers by years of service (8, 12 and 16 days is the common pattern)? It stays a per-person entitlement an administrator sets, as today.

---

## Review Notes

Plan review by the opus reviewer (14 Nov 2026), all CONFIRMED against the code unless marked:

| Issue                                                                                                                 | Perspective  | Severity | Affects Phase | Resolution                                                                                  |
| --------------------------------------------------------------------------------------------------------------------- | ------------ | -------- | ------------- | ------------------------------------------------------------------------------------------- |
| The 30-day policy cap refuses a 98-day maternity request before any type rule runs                                    | Correctness  | High     | 7q-a1         | A type's `MaxDaysPerRequest` replaces the policy cap                                        |
| Existing tests never seed a catalogue table, so "unchanged" was impossible                                            | Testing      | High     | 7q-a0         | Defaults live in code (`LeaveTypeCatalog` falls back like `CompanyPolicyReader`)            |
| Calendar-day hours are split wrongly across New Year (`HoursInYear` ignores stored hours)                             | Correctness  | High     | 7q-a1         | Every caller passes the type's schedule; shared vectors for both stacks                     |
| Auto-approve skips the own-decision check, so a configurable flag would extend it to 98 days                          | Security     | High     | 7q-a1         | `AutoApprove` only for types with a pool                                                    |
| Health and family facts reach every approver, lock screens and the withdrawal and auto-reject messages                | Privacy      | High     | 7q-a1         | `Sensitive` flag, on by default for the new types                                           |
| Changing a type's pool or day counting retroactively refunds or recharges history                                     | Data         | High     | 7q-a1         | Locked once the type has leave                                                              |
| "No code compares a type name" was false (`no-show` retype rule, sick patterns)                                       | Architecture | Med      | 7q-a0         | Locked system types                                                                         |
| A deactivated type would block approving its pending leave                                                            | Correctness  | Med      | 7q-a1         | Active only required on create and retype                                                   |
| Hours per day is company-wide; the frontend hard-codes 3 days notice; the endpoints and DTO changes were inconsistent | Accuracy     | Med      | 7q-a0/b/c     | Corrected above; `PUT` replaces the whole catalogue; `pools[]` added next to the old fields |
| `RequiresReference` in free-text notes is fragile                                                                     | Scope        | Low      | 7q            | Cut until Phase 9                                                                           |
| Estimate too low; one sub-phase too big                                                                               | Planning     | Med      | all           | Re-split into 7q-a0, a1, b, c (4 to 5 days)                                                 |

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] CLAUDE.md "Leave rules", the 8c plan and the Phase 8 doc lines updated
