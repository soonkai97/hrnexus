# Phase 7: Remove clock-in/out (shift log) feature

> **Status:** `Complete` (merged as #61 on 2026-11-13; the drop migration is not deployed, no production exists)
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** None

---

## Objectives

1. No WorkLog / clock-in/out code, routes, nav entries, i18n keys, or tests remain in the repo.
2. Everything that derived pay or hours from WorkLogs is removed with it (no orphaned, always-zero screens).
3. A new migration drops the WorkLog tables and WorkLog-only columns; existing data is backed up first.
4. All remaining tests green; app builds and deploys cleanly.

---

## Scope

### In scope
- **Staff app:** `features/log-shift/`, `/log-shift` route, shell nav entry, home-page active/recent shifts.
- **Admin app:** dashboard "tonight" hours + Est. Labour Cost cards + Close shift; staff-detail worklogs tab; reports workforce/labour-cost split; payroll export/payslip/earnings screens that read WorkLogs.
- **Shared-lib:** `WorkLogService`, `WorkLogDto`, worklog parts of `AdminService`, `SignalRService` shift events, `worklog.manage` permission, public-api exports.
- **Backend:** `WorkLog` entity + configuration, `Application/WorkLog/*`, `ShiftHours`, `ShiftPay`, `RoleShiftLimit`, `ShiftHub`, `ShiftJobs` (long-shift reminder), WorkLog queries in Dashboard/Report/Payroll/Earnings/Payslip, `WorkLogController`, shift constants, `worklog.manage`.
- **Role:** `MaxShiftDays` (shift-only). `HourlyRate` / `PayType` are **kept** pending Phase 8 (payroll rebuild).
- **Tests:** all WorkLog/shift specs (Application, Integration, E2E, spec.ts), plus seed/load scripts that clock in.
- **Docs:** CLAUDE.md pay-model + location-guard sections, STATE.md.
- **DB:** one new drop migration (WorkLogs table, indexes, WorkLog-only columns).

### Out of scope (deferred)
- Replacement pay model (monthly salary, allowances, deductions, payslips) → Phase 8.
- `StaffLocation` assignment: keep the entity; its only enforcement point was clock-in. Decide in Phase 8 whether locations stay as a staff attribute (default: keep, used for rosters/reports).
- Payroll locks: keep if they do not read WorkLogs (verify in step 1); otherwise handled in Phase 8.

---

## Scope decisions (2026-11-13)

- User rule: remove everything that exists only because of clock-in/out; keep anything with an independent purpose.
- **Migration `DropWorkLogs` (approved):** drops the `WorkLogs` table (indexes and FK to Staff go with it) and `Roles.MaxShiftDays`. Created, not yet deployed. Deploy needs a fresh DB backup and a maintenance window.
- **Kept for now (payroll concepts, redefined in Phase 8):** `Role.HourlyRate`/`PayType`, `RoleRates`, payroll locks (the lock guard currently has no callers), cash advances, `payroll.*` / `reports.view` permission keys.
- **Follow-ups:** rename `ShiftHub` / `/hubs/shift` to a notification name (FE and BE together); remove stale shift wording from locale files; Locations removal is phase 7j; English-only is phase 7i.

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Fresh DB backup (`scripts/backup-db.sh`) before migration deploy | Infra | Pending |
| Maintenance window for the drop migration | Infra | Pending |
| User sign-off on pay-model question below | Decision | Pending |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Dropping WorkLogs is irreversible; historical hours/pay lost | High | High | Take DB backup; optionally export WorkLogs to CSV first (ask user) |
| Payroll/reports/dashboard left half-wired, showing zeros | Med | High | Remove WorkLog-derived pay screens in the same phase; Phase 8 rebuilds payroll |
| Hidden callers of `locations/mine`, `PayrollLockGuard`, `ShiftHub` | Med | Med | Grep each before deleting; build + full test run after each layer |
| Audit trail rows referencing WorkLog entity | Low | Med | Keep existing audit rows; only stop writing new ones |
| Stale PWA cache keeps `/log-shift` | Low | Low | SW update + Ctrl+Shift+R after deploy |

---

## Implementation Steps

Order: leaf code first, schema last, so every commit builds.

1. Verify open coupling (read-only): `PayrollLockGuard`, `GetPayrollLocksQuery`, `locations/mine` callers, `INotificationHub` shift events — S
2. Frontend, staff-app: remove log-shift feature, route, nav, home shift widgets, i18n (en/zh/vi), specs — M
3. Frontend, admin-app: remove dashboard shift cards, staff-detail worklogs tab, reports labour-cost parts, payroll screens fed by WorkLogs, i18n — M
4. Shared-lib: remove `WorkLogService`/`WorkLogDto`/admin worklog methods/SignalR shift events/`worklog.manage`; fix `public-api.ts` — S
5. Backend application layer: delete `Application/WorkLog`, shift helpers, WorkLog branches in Dashboard/Report/Payroll/Earnings/Payslip queries; delete their tests — L
6. Backend API/infra: `WorkLogController`, `ShiftHub`, `ShiftJobs`, DI registration, `Program.cs` hub mapping, constants, `Role.MaxShiftDays` + endpoints — M
7. Domain + persistence: delete `WorkLog` entity/configuration/`DbSet`; add `DropWorkLogs` migration — M (**confirm with user before generating**)
8. E2E, scripts, docs: delete `log-shift.spec.ts`, fix home/admin specs, trim `seed-and-demo-hardening.ps1` / `load-test-50-live.ps1`, update CLAUDE.md + STATE.md — S
9. Full verification (below), PR, squash-merge, Docker rebuild `--no-cache`, deploy in maintenance window — M

Rough effort: about one focused day; steps 5 and 7 carry the risk.

---

## Validation Criteria

- [x] `grep -ri "worklog\|log-shift\|ShiftHours\|clock-in"` over `projects/`, `backend/src`, `tests/` returns nothing (except historical migrations and one guard-test example commit message)
- [x] `npx ng test shared-lib|admin-app|staff-app --no-watch` green; counts drop only by removed specs
- [x] `dotnet test` green (Application, Integration, Infrastructure)
- [x] `npx ng build` clean for both apps
- [x] Coverage scan (Rule 17) returns 0 spec gaps
- [ ] `npm run test:e2e` green against rebuilt Docker (not run: no Docker stack in CI or here)
- [ ] Migration applied on a copy of prod data with no errors (not applicable: no production data exists)
- [ ] Multi-perspective review completed (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge commit.
- **Data:** restore from the pre-migration backup via `scripts/restore-db.sh` (destructive; prompts YES). Without the backup the WorkLogs data cannot be recovered.

---

## Review Notes

_Filled in after phase completes._

### Issues found
| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|

### Plan updates
_How did this phase's findings change Phase 8?_

---

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] Phase 8 plan updated based on findings
- [x] CLAUDE.md updated (pay model + location guard sections)
