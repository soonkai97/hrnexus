# Phase 7q: Leave types as company data (annual, sick, hospitalisation, maternity, paternity, compassionate)

> **Status:** `Draft`
> **Created:** 2026-11-14 | **Updated:** 2026-11-14
> **Depends on:** Phases 7l and 7m merged (leave rules, company policy). Independent of Phase 8 apart from one rule: **payroll deducts pay only for leave whose type says `DeductsPay`** (see "Payroll"), so this phase should land before 8c.
> **Why:** the owner asked (14 Nov 2026) for more leave types: **annual, sick, hospitalisation, maternity, paternity and compassionate**. Today the product has five hard-coded types (`annual`, `sick`, `emergency`, `unpaid`, `no-show`) and every rule that differs between them (balances, auto-approval, advance notice, which pool is charged) is a `==` comparison in about 15 places. Adding four more the same way would copy that again, and the next company will want different ones. Phase 7m's rule applies: rules that differ between companies are settings, not constants.

---

## Objectives

1. A company's **leave types are data** (a catalogue), editable by someone with `policy.manage` on the company policy page. Each type says how it behaves; no code compares a leave-type name.
2. The owner's types exist from day one: annual, sick, **hospitalisation, maternity, paternity, compassionate**, plus the existing emergency, unpaid and no-show, so nothing that works today stops working.
3. **No behaviour change for the five existing types** on an untouched installation: the default catalogue reproduces today's rules exactly, proven by the existing leave test suite passing unchanged.
4. Staff apply for any **active** type from one list; managers see the same list; balances show each type's allowance and what is left.
5. Payroll learns from the catalogue which leave **deducts pay** (unpaid and no-show), so it never has to know leave-type names.

---

## What is hard-coded today (verified on `master`)

| Rule                                                                                                                          | Where                                                                                                                                                        | Becomes                                                                 |
| ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------- |
| The five type names and the list that validates them                                                                          | `AppConstants.Leave`, `UpsertLeaveCommand.ValidateRequest`, the frontend `LEAVE_TYPES` in `leave.component.ts` and `my-leave.component.ts`, `leave.model.ts` | the catalogue                                                           |
| Sick and emergency leave are auto-approved when they are applied for                                                          | `UpsertLeaveCommand` (`isSick`, `isEmergency`)                                                                                                               | `AutoApprove` flag                                                      |
| Annual leave needs `AnnualLeaveAdvanceDays` of notice                                                                         | `UpsertLeaveCommand.ValidateRequest`, `my-leave.component.ts`                                                                                                | `AdvanceNoticeDays` on the type (annual uses the policy value)          |
| Two balance pools: annual (which **includes emergency**) and sick, each a per-person yearly entitlement in `LeaveEntitlement` | `EnsureSufficientBalanceAsync`, `GetLeaveBalanceQuery`, `GetAllLeaveBalancesQuery`, `GetLeaveSummaryQuery`, `CarryOverLeaveEntitlementCommand`               | `PoolKey` on a type; annual and sick keep their per-person entitlements |
| `unpaid` has no balance; `no-show` is admin-only and cannot be applied for                                                    | `LeaveController`, `BulkMarkLeaveNoShowCommand`                                                                                                              | `HasBalance = false`, `AdminOnly = true`                                |
| Sick-leave pattern warnings (many Monday or Friday sick days)                                                                 | `GetLeavePatternWarningsQuery`                                                                                                                               | stays keyed to the `sick` type (decision 6)                             |
| Colours and labels per type                                                                                                   | `leave.component.html/scss/ts`, `my-leave.component.*`                                                                                                       | `Label` and `Colour` on the type                                        |
| Gender                                                                                                                        | not stored anywhere                                                                                                                                          | **not modelled**: see decision 5                                        |

---

## Design

### The catalogue

- **`LeaveTypeDefinition`** (single table, one row per type; seeded with defaults; edited on the policy page):
  - identity: `Key` (immutable slug, for example `maternity`; the value already stored on every `LeaveRequest`), `Label`, `Colour` (a small fixed set of tag colours), `SortOrder`, `Active`.
  - **balance:** `Pool` (`annual`, `sick`, `own`, or `none`). `annual` and `sick` use the existing per-person `LeaveEntitlement`; **`own`** means the type has its own yearly allowance for everyone (`AllowanceDays`, the same for every person, for example hospitalisation 60); `none` has no balance (unpaid, no-show, maternity and paternity, which are per event: see below).
  - **limits:** `MaxDaysPerRequest` (nullable; maternity 98, paternity 7, compassionate 3), `AllowanceDays` (for `own` pools).
  - **behaviour flags:** `AutoApprove`, `AdvanceNoticeDays` (nullable; annual says "use the policy value"), `DeductsPay` (payroll), `AdminOnly` (only a manager records it), `CountsCalendarDays` (consecutive calendar days instead of working days, used by maternity and paternity), `RequiresReference` (a certificate or reference number must be written in the notes; **file upload waits for Phase 9**).
  - audit fields and a version token, like `CompanyPolicy`.
- **Rules a type must obey** (domain invariants, tested): the key is a lowercase slug and never changes; a key can be deactivated but never deleted while any leave uses it (history stays readable); `Pool = own` needs `AllowanceDays`; `AdminOnly` types cannot be `AutoApprove`; at least one type stays active; days are 0.5 to 366.
- **`LeaveTypeCatalog`** (Application, like `CompanyPolicyReader`): reads the catalogue once per request; every leave handler asks it instead of comparing names.

### Default catalogue (what a new or untouched install gets; the owner's list plus today's types)

| Key               | Pool                                            | Allowance / limit                                      | Flags                               |
| ----------------- | ----------------------------------------------- | ------------------------------------------------------ | ----------------------------------- |
| `annual`          | `annual`                                        | per-person entitlement (today)                         | advance notice from the policy      |
| `emergency`       | `annual` (charged to the annual pool, as today) | none                                                   | auto-approve                        |
| `sick`            | `sick`                                          | per-person entitlement (today)                         | auto-approve; sick-pattern warnings |
| `hospitalisation` | `own`                                           | **60 days a year (ASSUMED)**                           | reference required                  |
| `maternity`       | `none`                                          | **98 consecutive calendar days per request (ASSUMED)** | calendar days, reference required   |
| `paternity`       | `none`                                          | **7 consecutive calendar days per request (ASSUMED)**  | calendar days                       |
| `compassionate`   | `own`                                           | **3 days a year (ASSUMED: no statutory figure)**       | none                                |
| `unpaid`          | `none`                                          | none                                                   | deducts pay                         |
| `no-show`         | `none`                                          | none                                                   | deducts pay, admin only             |

The figures marked ASSUMED are general knowledge of the Employment Act and common practice, **not** confirmed for this company: counsel or the accountant confirms them, and the company changes them on the policy page (data, not code). Existing five types keep their keys and behave exactly as today.

### Behaviour

- **Applying:** the type must be active, not `AdminOnly` for staff, within `MaxDaysPerRequest`, notice honoured, a reference written when required. Hours are charged by the type's day counting (working days by the company schedule, or every calendar day for the calendar-day types).
- **Balances:** a type with `Pool = own` has `AllowanceDays` x the person's working-day hours as its yearly allowance, charged per year exactly like annual and sick (including a request that crosses New Year). Per-event types (`none`) are limited only by `MaxDaysPerRequest`; a manager approves each one.
- **The balance screens** list every type that has a balance, with allowance, used and remaining, driven by the catalogue instead of fixed columns.
- **Changing a type is not retroactive:** stored leave keeps its hours; a changed allowance or limit applies to requests from then on (the same rule as Phase 7m).

### Frontend

- A `LeaveTypeService` (shared-lib) loads the **active** types once per session, like the company policy; both leave pages build their type lists, tag colours, hints ("needs 3 days' notice", "enter the certificate number") and validation from it. The hard-coded `LEAVE_TYPES`, label maps and colour ternaries go.
- **Policy page:** a "Leave types" section: a table (label, key, balance, limit, flags, active) and an edit dialog with the same client checks as the server; a type in use cannot be deleted, only deactivated.

### Payroll

Phase 8c reads "leave that deducts pay" from the catalogue (`DeductsPay = true`, today unpaid and no-show) instead of naming types. Paid types (annual, sick, hospitalisation, maternity, paternity, compassionate) never change the payslip. The 8c plan is updated in the same PR.

---

## Decisions made in this plan (change them here if wrong)

1. **One catalogue per installation**, edited by `policy.manage`; no per-department or per-person types.
2. **Emergency stays** (the owner's list did not mention it; removing it would change today's behaviour). It is simply a catalogue row an administrator can deactivate.
3. **Balances stay per-person for annual and sick** (so Phase 7l's entitlement and carry-over work unchanged); the new pooled types use one company-wide allowance each.
4. **Maternity and paternity are per event, with no yearly balance.** The rule is "at most N consecutive calendar days per request" plus a manager's approval. Tracking the number of confinements or the child's date of birth is not modelled.
5. **No gender-based eligibility.** The product stores no gender, and adding it is a privacy decision of its own. Maternity and paternity are therefore recorded by a manager or applied for and approved like any other leave; the approver checks eligibility.
6. **Sick-pattern warnings** keep working for the `sick` key only (a data-driven "watch this type" flag is not worth a column yet).
7. **Hospitalisation is its own pool**, separate from sick leave. The Employment Act is believed to count hospitalisation days together with outpatient sick days toward a combined limit; counsel confirms, and a combined pool would be a follow-up.
8. **Documents:** `RequiresReference` makes a certificate or reference number mandatory in the notes. Uploading the certificate itself is Phase 9.
9. **Public holidays and replacement leave** are out of scope (they need the company holiday calendar, deferred since 7l).

---

## Scope

### In scope

- Backend: `LeaveTypeDefinition`, migration with the default catalogue, `LeaveTypeCatalog`, every leave handler and query rewired off type names (the 15 places above), validation, endpoints (`GET /api/leave-types` for anyone signed in; `PUT` for `policy.manage`), audit.
- Frontend: `LeaveTypeService`, both leave pages and the balance views driven by it, the policy page section.
- Tests: the existing leave suite unchanged and green on the default catalogue (parity), a test per flag and per new type, catalogue validation, E2E that adds a custom type and uses it.
- Docs: CLAUDE.md "Leave rules", the 8c plan line, an ADR note.

### Out of scope (deferred)

- Gender or eligibility rules, confinement counting, certificate upload (Phase 9), the combined sick and hospitalisation limit, public holidays, replacement leave, tenure bands for annual entitlement, effective-dated catalogue history.

---

## Risks

| Risk                                                                           | Likelihood | Impact | Mitigation                                                                                                                                                     |
| ------------------------------------------------------------------------------ | ---------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A missed hard-coded comparison keeps an old rule alive for new types           | Med        | Med    | A grep guard test for `AppConstants.Leave.*` type constants outside the seed; every handler takes the type's definition                                        |
| The refactor changes behaviour for the five existing types                     | Med        | High   | The existing leave tests (about 100) run **unchanged** against the default catalogue before anything new is added; E2E covers apply, approve, cancel           |
| Calendar-day counting for maternity and paternity breaks the working-day maths | Med        | Med    | The type's day counting picks the schedule passed to the existing `LeaveHoursCalculator` (all seven days); vectors added to the shared fixture for both stacks |
| A catalogue edit strands existing leave (a type deactivated or renamed)        | Low        | Med    | Keys are immutable; deactivate only; history keeps reading old keys with their stored label                                                                    |
| The ASSUMED statutory figures are wrong for this company                       | Med        | Med    | Marked in the seed notes and on the page; counsel or the accountant confirms; they are data                                                                    |
| Payroll double-counts or misses leave that deducts pay                         | Low        | High   | 8c reads `DeductsPay` from the catalogue and has a test per flag                                                                                               |

---

## Implementation steps

**7q-a: backend with parity (about 1.5 days)**

1. `[TDD]` `LeaveTypeDefinition` aggregate (invariants above) and the default catalogue; `LeaveTypeCatalog`.
2. **Parity first:** rewire `UpsertLeaveCommand`, the three balance queries, the summary, carry-over and the controllers to read the catalogue; run the **existing** leave suites unchanged on the default catalogue until green.
3. `[TDD]` New behaviour, test first: each flag (`AutoApprove`, `AdvanceNoticeDays`, `AdminOnly`, `MaxDaysPerRequest`, `RequiresReference`, `CountsCalendarDays`, `DeductsPay`), `Pool = own` balances across New Year, hospitalisation, maternity (98 calendar days across a weekend), paternity, compassionate, an inactive type refused, an unknown type refused.
4. Migration (table and seed), `GET` and `PUT /api/leave-types` with version token, audit; Integration tests for the endpoints and the migration on a database with existing leave.

**7q-b: frontend, E2E, docs (about 1 day)** 5. `[TDD]` `LeaveTypeService`; both leave pages and balance views driven by it; hints and validation from the type. 6. Policy page "Leave types" section. 7. E2E: an administrator adds a custom paid type, a staff member applies for it and a manager approves; an employee cannot apply for an admin-only type; the default types still work. 8. CLAUDE.md, the 8c plan line, ADR note; multi-perspective review.

---

## Validation criteria

- [ ] `git grep -n "AppConstants.Leave.\(Sick\|Annual\|Unpaid\|NoShow\|Emergency\)"` finds only the seed and the migration
- [ ] The existing leave test suite passes unchanged on the default catalogue
- [ ] All nine default types exist; the four new ones can be applied for, approved and show on balances and the calendar
- [ ] A maternity request of 98 calendar days across weekends is accepted and one of 99 is refused; paternity 7 and 8 likewise
- [ ] Hospitalisation draws on its own 60-day allowance across New Year; compassionate on its 3-day allowance
- [ ] An administrator can add, edit and deactivate a type; a type with leave cannot be deleted; staff only see active types
- [ ] `dotnet test` (all three projects), `npx ng test`, builds, ESLint, Prettier ratchet, `dotnet format` clean; E2E green against the rebuilt stack
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the sub-phase PRs in reverse order.
- **Data:** the migration adds one table and seeds it; its `Down` drops it. Existing leave keeps its `LeaveType` key, so nothing stored changes shape.

---

## Open questions

1. **Statutory figures:** are hospitalisation 60 days a year, maternity 98 consecutive days, paternity 7 consecutive days and compassionate 3 days right for the company? (They are seeded as assumptions.)
2. **Sick and hospitalisation:** do hospitalisation days count toward the same limit as sick days, or are they separate (assumed separate)?
3. **Emergency:** keep it (assumed) or deactivate it for this company?
4. **Eligibility:** is a manager's check enough for maternity and paternity, or does the company want the product to know gender or marital status (a privacy decision of its own)?
5. **Annual entitlement by service:** does the company grant annual leave in tiers by years of service (8, 12 and 16 days is the common pattern)? It stays a per-person entitlement an administrator sets, as today.

---

## Review Notes

_Filled in after the phase completes._

| Issue | Perspective | Severity | Affects Phase | Resolution |
| ----- | ----------- | -------- | ------------- | ---------- |

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] CLAUDE.md "Leave rules" and the 8c plan updated
