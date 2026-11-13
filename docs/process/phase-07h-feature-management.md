# Phase 7h: Feature management (module switches)

> **Status:** `Draft` (design only, nothing built)
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phase 7 (remove shift log), 7c (architecture tests), 7d (ProblemCodes); best after 7f (dispatcher): `ListModules` can be the 7f pilot slice
> **Reference:** IMS `docs/architecture/adr-modules-and-tracking-dimensions.md` section 4, `ModuleCatalog`, `RequiresModule`, `ModuleGateMiddleware`, `Features/Modules`

---

## Objectives

1. An admin can switch optional HRM modules on/off per installation from a **Features** page, with a reason, audited.
2. A switched-off module is invisible everywhere: API answers 404, nav and routes vanish, its permissions are hidden. Data is kept.
3. One source of truth (a code catalog) plus a test that fails when a gated surface is missing its gate. This is what the old system lacked.
4. Production behaviour is unchanged on deploy (existing modules seeded ON).
5. Phases 8-12 each ship behind their own module switch from day one.

---

## What the old system was, and why it went

| When | Commit | What happened |
|---|---|---|
| 2026-05-11 | `AddFeatureFlags` + 2 seed migrations | `FeatureFlags(Key, IsEnabled, UpdatedAt)` table, 13 keys seeded OFF across three migrations; `[FeatureRequired(...)]` (OR semantics) + global `FeatureFlagFilter` returning **403** `{error}`; `GET /api/feature-flags` for any signed-in user, 60 s `IMemoryCache`; FE `FeatureFlagService` + `featureFlagGuard` + nav `featureFlag` field + `PERMISSION_FEATURE_FLAG` map hiding permissions in the editors |
| by 2026-05-25 | admin **Features page** | Cards per phase with toggle switches, enable/disable all. Writes (`PUT /api/feature-flags`) needed a **PIN**: `FeatureMgmtService` (PIN hash + `FeatureMgmt__TokenSecret` in env, 4 h unlock JWT in `X-Feature-Access`, in-memory per-IP lockout) |
| 2026-05-25 | `cb4e7cd` | **UI + PIN removed**: "Feature flags are now managed directly via DB queries". Toggling became a manual SQL job with no audit |
| 2026-05-31 | `c3f68f2` | Audit found the flag gates had **drifted between layers** (a ledger API ungated while its page was gated; a shared endpoint needing OR of two flags) |
| 2026-06-01 | `e49b66d` | **System deleted**: "collapsed to a single flag (`payroll`) and was no longer earning its complexity". Tips, Announcements, Messaging, Staff Ledger and Pay Summary had each been removed as features, so their flags died with them; stale rows had needed hand-written clean-up migrations |

**Root causes to design against:** (a) flags were added before features were stable, then outlived them; (b) every flag had to be hand-wired in four places (nav, route guard, controller attribute, permission map) with nothing to catch drift; (c) toggling had either a heavy bespoke PIN scheme or no UI at all, and never an audit trail; (d) 403 revealed that a disabled feature exists.

**How this design avoids them:**
- A module enters the catalog **only when its slice ships** (no speculative flags). Removing a feature deletes its catalog entry in the same PR; a test fails on a `[RequiresModule]` code that is not in the catalog.
- Gating is metadata + one middleware + one FE guard; an **architecture test** lists the endpoints that must carry the gate (7c shrinking-allowlist style), and a FE spec checks every gated route has `requireModule`. Permission hiding comes from `PermissionDefinition.Module`, not a hand-kept map.
- No PIN, no extra secret. Flipping needs the normal `modules.manage` permission, a reason (at least 5 chars), and writes an audit event with who/before/after.
- Off = **404** (not 403), decided before authorization.

---

## Scope

### In scope
- Module catalog (code), `SystemSettings` key/value table with `Module.<code>` rows, `IModuleSwitches` service, `ModuleGateMiddleware`, `[RequiresModule]`.
- `GET /api/modules` (any signed-in), `PUT /api/modules/{code}` (`modules.manage`, reason, audit, dependency + disable checks).
- Permission hiding for disabled modules (catalog + role/staff editors), hidden grants preserved on role save.
- Admin **Features** page + FE `ModulesStore`, `requireModule` guard, nav hiding in both shells, live refresh via SignalR.
- i18n en/zh/vi (admin-app + staff-app), unit/integration/architecture/E2E tests.

### Out of scope (deferred)
- Per-venue/tenant switches (installation = one company, as in IMS).
- Switching `leave`, staff, roles, locations (core). See question 1.
- Percentage rollouts, per-user flags, anonymous config endpoint.
- Building the HRM modules themselves (Phases 8-12 register their codes).

---

## Module catalog (HRNexus)

Catalog is code (`HRNexus.Domain.ModuleCatalog`); codes are lower-case, stable, never renamed. A module without a row is **off**; a code not in the catalog is never on.

| Code | Surface it gates | Ships | Default after deploy | Disable refused while | Requires |
|---|---|---|---|---|---|
| `cash-advances` | admin `/cash-advances`, CashAdvances API, `cashadvance.manage` | now | **ON** (seeded, behaviour unchanged) | open (unrecovered) advances exist | none (Phase 8 may add `payroll`) |
| `payroll-locks` | admin `/payroll-locks`, PayrollLocks API, `payroll.lock` | now | **ON** (seeded) | never (data kept; `PayrollLockGuard` still enforces locks: a switch hides the surface, never invariants) | none (Phase 8 may add `payroll`) |
| `payroll` | payroll runs, payslips, deductions | Phase 8 | ON for existing venues | draft/open runs | none |
| `documents` | employee documents, expiry alerts | Phase 9 | OFF | never | none |
| `onboarding` | onboarding/offboarding checklists | Phase 10 | OFF | open checklists | none |
| `recruitment` | postings, candidate pipeline | Phase 11 | OFF | open postings | none |
| `performance` | review cycles, goals | Phase 12 | OFF | open cycles | none |

Only the first two rows are in the catalog at the end of 7h; the rest are listed so the gate's shape is proven, and each phase adds its row and its test. Core (never gated): auth, staff, roles, locations, dashboard, notifications, account, leave (question 1), the modules endpoints themselves.

**Who may toggle:** new permission `modules.manage` (core, **dangerous**, SUPERADMIN only by default, granted by a migration like earlier permission additions). Reading `GET /api/modules` is open to any signed-in user because the FE needs it.

---

## Backend design (IMS style)

| Part | Decision |
|---|---|
| Catalog | `ModuleCatalog.All` of `ModuleDefinition(Code, Name, Description) { Requires }`, `ModuleCodes` constants. Tests: unique codes, `Requires` acyclic and only catalog codes, code length |
| State | New additive table `SystemSettings(Key varchar(100) PK, Value varchar(500), UpdatedAt)`; one row `Module.<code>` = `true`/`false`. Same shape as IMS `SystemSetting`; Phase 8+ settings reuse it. Seed migration inserts `Module.cash-advances=true`, `Module.payroll-locks=true` so production behaviour is identical |
| Read | Scoped `ModuleSwitches` reads the few `Module.*` rows once per request; `Invalidate()` after a flip. No cross-request cache (single API container; the old 60 s cache caused "changes take a minute"). Add `IMemoryCache` with explicit invalidation only if measured |
| Gate | `[RequiresModule("code")]` on controller/action; `ModuleGateMiddleware` between `UseAuthentication()` and `UseAuthorization()`: no token goes on (401); signed-in caller of a disabled module gets **404** whatever their permissions; enabled goes on to the permission policy (403/200). Endpoints without the attribute are untouched. Both controller and action codes must be on |
| Endpoints | `GET /api/modules` -> `[{ code, name, description, enabled, requires }]`; `PUT /api/modules/{code}` body `{ enabled, reason }`, policy `modules.manage`. Idempotent (already in state = 200, nothing written, no audit) |
| Flip handler | Under a `modules` advisory lock (`pg_advisory_xact_lock`, no retry: busy = 409); validates reason; enabling needs every `Requires` module on; disabling refused (409 + count) if a dependent module is on or the module's `IModuleDisableCheck` finds open data; writes `module.enabled`/`module.disabled` through `IAuditService` with the caller (performer `null` = authenticated caller) |
| Errors | ProblemCodes (7d): `module_not_found` (404), `module_reason_required` (400), `module_needs_others` / `module_is_needed_by` / `module_has_open_data` (409). Gate itself returns the plain not-found response |
| Permissions | `PermissionDefinition.Module` (null = core). `GET /api/permissions` omits codes of disabled modules; role/staff-override writes reject a code of a disabled module (400) and **preserve the existing hidden grants** (merge, never audited as removed), so re-enabling restores access |
| Live update | After a flip, broadcast `modulesChanged` on the existing SignalR notification hub (`/hubs/shift`, kept by Phase 7; rename tracked separately) so open clients refetch `GET /api/modules`. A stale client hitting a disabled endpoint gets 404; the FE interceptor refetches the list and shows "This feature was turned off" |
| Data | Kept and hidden. Switching never deletes or archives, and no posting/validation invariant reads the switch |

DB changes are additive (new table + seed + one permission grant). They still follow the CLAUDE.md rule: fresh backup first, deploy via `./deploy.sh` in the maintenance window. Phase 7's drop migration is separate and ships first.

---

## Frontend design

| Part | Decision |
|---|---|
| Store | shared-lib `ModulesStore` (signals): `load()`, `isEnabled(code)`, `modules()`, refetch on `modulesChanged`, on shell start and on the interceptor's module-404. Loaded in both shells like `PermissionService.load()` |
| Guard | `requireModule(code)` next to `permissionGuard`: waits for the first load, then sends to the not-found/dashboard route (not the 403 route). Applied to `cash-advances` and `payroll-locks` routes |
| Nav | `NavItem.module?`; shown only if the module is on AND the permission is held (admin shell). Staff shell reads the same store |
| Features page | admin route `/features`, `permissionGuard('modules.manage', '/dashboard')`, nav group `setup`. Card per module: icon, localized name/description, `requires` chips, `p-toggleswitch`. Toggling opens a small dialog with a required **reason** textarea (min 5 chars) and, on turn-off, the blocker text from a 409. Skeleton on load, button spinner on save, toast with `err.error` message. `appendTo="body"` on overlays |
| Permission editors | read the filtered catalog from the API (no FE map) |
| i18n | `features.*` keys (title, subtitle, toggle labels, reason prompt, errors, `modules.<code>.name/desc`) in admin-app en/zh/vi; staff-app gets `common.featureOff`. Server sends English name; FE translates by code |
| Forms | template-driven pattern from CLAUDE.md |

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Phase 7 merged (payroll export/payslips removed; `payroll-locks`, `cash-advances` remain) | Code | Pending |
| 7c Architecture.Tests (endpoint-must-be-gated rule, catalog rules) | Code | Pending |
| 7d ProblemCodes + typed domain exceptions | Code | Pending |
| 7e operation ids (`Modules_list`, `Modules_switch`) | API | Pending |
| 7f `IDispatcher` (pilot slice `ListModules`) | Code | Pending, soft |
| SignalR hub reachable from both shells | Infra | Done |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Repeats the old "single flag" collapse (switches for features nobody toggles) | Med | Med | Catalog entry only with a shipped slice; Phases 8-12 give 5 more real modules; review each module with "would an installation ever turn this off?" |
| Gate drift between API, route, nav (the `c3f68f2` bug) | Med | High | Architecture test per gated controller/action; FE spec per gated route; one `ModuleCodes` source mirrored in a FE const checked by a contract test |
| Prod behaviour changes on deploy (module defaults OFF) | Low | High | Seed ON rows in the migration; migration test asserts rows exist; E2E smoke of `/cash-advances` and `/payroll-locks` after deploy |
| Admin locks themselves out of the Features page | Low | Med | Modules endpoints and `modules.manage` are core and never gated |
| Hidden permission grants lost on role save | Med | Med | Merge logic + test (save while module off, re-enable, grants back) |
| Switch off while data is open | Med | Med | `IModuleDisableCheck` 409 with counts; data kept |
| Maintenance-window/backup rule skipped for the migration | Low | Med | Listed in the deploy checklist of the phase |

---

## Implementation Steps

Steps marked `[TDD]` write the failing test first (Rule 14).

1. `[TDD]` `ModuleCatalog`, `ModuleCodes`, `ModuleDefinition`, `ModuleDependencies` + catalog tests (unique, acyclic, known codes) - S
2. `SystemSettings` entity/config + additive migration with seed rows + `modules.manage` grant; migration test for seeds - S
3. `[TDD]` `IModuleSwitches`/`ModuleSwitches` (missing row = off, unknown code = off, `Invalidate`) - S
4. `[TDD]` `RequiresModuleAttribute` + `ModuleGateMiddleware` in `PipelineSetup`; integration tests: 401 anonymous, 404 signed-in without permission, 404 with permission, 403 enabled-without-permission, 200 - M
5. `[TDD]` `ListModules` query + `SwitchModule` command (reason, idempotent, dependencies, disable checks, lock, audit, ProblemCodes) + `ModulesController` - M
6. `[TDD]` Permission hiding (`PermissionDefinition.Module`, catalog filter, role/override write merge) - M
7. Apply `[RequiresModule]` to CashAdvances and PayrollLocks controllers; architecture test "gated endpoints carry the gate; core controllers never do" (7c style allowlist) - S
8. SignalR `modulesChanged` broadcast after flip - S
9. `[TDD]` shared-lib `ModulesStore`, `requireModule` guard, interceptor 404 refetch + specs - M
10. Shell nav gating (admin + staff) and route guards; i18n keys en/zh/vi - S
11. `[TDD]` Features page + dialog + specs (stubbed routes, no `detectChanges`, per CLAUDE.md pattern) - M
12. E2E: admin turns `cash-advances` off with a reason, API 404, nav entry gone, turns it on, state restored in `afterAll`; permission-hidden test - S
13. Docs: CLAUDE.md "Feature management" section, ADR 0002 `docs/architecture/adr-modules.md` (adapted from IMS), Rule 17 coverage scan (the stale `FEATURE_TOKEN_SECRET` and PIN variables were already removed from `.env.example`) - S

**Size:** about 2 days (backend 1 day, frontend 0.75 day, E2E/docs 0.25 day). Longest step: 4 and 6 (gate ordering and permission merge).

---

## Validation Criteria

All must pass before this phase is marked Complete:

- [ ] Gate order test: 401 / 404 / 403 / 200 as specified
- [ ] Deploy-safety test: after migration `cash-advances` and `payroll-locks` are ON and both routes work
- [ ] Architecture test fails when a gated controller loses its attribute (seeded violation) and when a `[RequiresModule]` code is not in the catalog
- [ ] Flip writes an audit row with caller, reason, before/after; unknown/idempotent/dependency/open-data cases covered
- [ ] Role save while a module is off preserves its grants
- [ ] E2E toggle round-trip green and restores state
- [ ] `npx ng test` (3 projects) and `dotnet test` green; test counts at or above baseline; Rule 17 scan 0 gaps
- [ ] `ng build` clean; ProblemCodes mapped in FE toasts
- [ ] Multi-perspective review completed (see `REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge; the `SystemSettings` table and seed rows are additive and harmless if left.
- **Specific:** if a module is wrongly OFF in production, flip it back on the Features page, or run `UPDATE "SystemSettings" SET "Value"='true' WHERE "Key"='Module.<code>'` after a backup.

---

## Review Notes

_Filled in after phase completes._

### Issues found
| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|

### Plan updates
_How do the findings change Phase 8 (first module built behind its own switch)?_

---

## Open questions

1. **Is `leave` switchable?** Recommended: no, keep it core (it feeds the staff home, dashboard, notifications and the `LeaveJobs` background jobs). If yes, add `leave` to the catalog with a disable check on pending requests and gate the staff home card (about +0.5 day).
2. **Who may flip?** Recommended: `modules.manage`, SUPERADMIN only, reason required, no PIN. Do you want a second factor (the old PIN) back for production, or is the audited permission enough?
3. **Live refresh:** recommended SignalR `modulesChanged` push plus 404-refetch. Accept a simple "next page load" refresh instead (saves about 0.25 day)?

---

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives from `REVIEW_CHECKLIST.md`)
- [ ] Phase 8 plan updated (first module behind a switch; `payroll-locks`/`cash-advances` `Requires` decision)
- [ ] CLAUDE.md updated (feature management pattern)
