# Phase 7j: Remove the Locations feature

> **Status:** `In progress` (code, tests, E2E, scripts and docs removed on `feat/remove-locations`; the `DropLocations` migration (step 7) waits for approval)
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phase 7 (remove shift log) merged. Must land **before** Phase 7i (English only) so the Location columns never need translating.
> **Environment note (13 Nov 2026):** no production deployment exists, so the backup, approval and maintenance-window steps below apply to the next deployment that holds data.

Measured on the Phase 7 commit `9f8aa3c` (post-WorkLog removal), not on master.

---

## Objectives

1. No Location / StaffLocation code, routes, nav, i18n keys, permission key or tests remain.
2. Staff add/edit/detail and the staff list work without any location field or bulk action.
3. `Locations` and `StaffLocations` tables are dropped by one migration, after a backup and approval.
4. All suites green; CLAUDE.md and README no longer describe locations.

---

## Footprint (real counts)

| Layer | What | Count |
|---|---|---|
| FE (projects/) | files matching the location patterns (strict) | 22 (6 are specs); 7 more files only hit `window.location` and are ignored |
| FE biggest | `location.store(.spec).ts` (18/78 refs), `locations.component.{ts,html,spec.ts}` (23/14/44), `staff.component.{ts,html,spec.ts}` (15/15/16), `staff-detail.component.{ts,html,spec.ts}` (12/7/4), `staff-management.service(.spec).ts` (6/11) | |
| FE shared-lib | `location.model.ts`, `LocationStore`, public-api + `models/index.ts` exports, `staff.model.ts` (`locationIds`), `staff.store.ts`, permission services (`locations.manage`) | 8 |
| FE i18n | admin-app en 22 location lines, zh 15, vi 15; staff-app 0 | 52 lines |
| FE routes/nav | `/locations` route in admin `app.routes.ts` + nav entry in admin `shell.component.ts` | 2 |
| BE src | Location entity (+`ImagePath`, `NameZh/NameVi`), StaffLocation, `LocationConfiguration`, `StaffLocationConfiguration`, `StaffConfiguration`, `AppDbContext`, `IAppDbContext`, `LocationsController`, `DeleteLocationCommand`, `BulkStaffCommands`, `DeleteStaffCommand`, `RegisterStaffCommand`, `UpdateStaffCommand`, `GetStaffByIdQuery`, `StaffController`, `AppConstants` (`LocationsManage`), `SeedData` | 18 files |
| BE tests | `BulkCommandTests` (22 refs), `TestDbContext` (5), `Integration.Tests/StaffDeleteCascadeTests` (5) | 3 files |
| BE migrations | history only, never edited | 72 files mention Location |
| E2E | `locations.spec.ts` (delete), `staff.spec.ts` (11), `negative.spec.ts` (6), `workflow.spec.ts` (6); `shared/` helpers have none | 4 specs |
| Scripts | `seed-and-demo-hardening.ps1` (6 lines seed a location) | 1 |
| Docs | CLAUDE.md 38 hits (about 8 false positives: nginx `location`, `Set-Location`), README 19, DEPLOY.md 1 | 3 |

**Safety check:** outside `Application/Locations` and `Application/Staff`, the only backend reference is `IAppDbContext`. Leave, notifications, payroll locks, reports and auth do not use locations. In the model snapshot the only FK to Locations is `StaffLocations`; `WorkLogs.LocationId` is a plain string (dropped by Phase 7), so the two drop migrations are independent. Removal is safe.

---

## Scope

### In scope
- Admin Locations page, `LocationStore`, `Location` model, location multiselects on staff add and detail, "Assign locations" bulk action, `locations.manage` permission (catalog, seed, grants).
- Backend: `Location`, `StaffLocation`, `Staff.LocationEntries/LocationIds/SetLocations/AddLocations`, `LocationsController` (incl. image upload), delete command, `BulkAssignLocationsCommand`, `Locations` params on register/update, `StaffDetailDto.LocationIds`, delete guards that mention locations, seed data.
- Tests, E2E, scripts, docs (CLAUDE.md "Staff ↔ location assignment" and the stale bulk-create-locations text, README).
- Drop migration for `Locations` + `StaffLocations`, and cleanup of orphan `locations.manage` permission grants.

### Out of scope (deferred)
- Any replacement for "where staff work" (department, roster, site): decide in the HRM phases, not here.
- `docs/hostess-*.html` legacy mockups.
- Phase 7i work (Roles keep their `NameZh/NameVi` until then).

---

## Dependencies

| Dependency | Type | Status |
|---|---|---|
| Phase 7 merged to master | Code | Pending |
| Fresh DB backup + maintenance window | Infra | Pending |
| User approval for the irreversible drop | Decision | Pending |
| Uploaded images under `/uploads/locations/` | Data | Decide in open question 1 |

---

## API contract changes (breaking)

- Removed: `GET/POST/PUT/DELETE /api/locations`, `GET /api/locations/mine`, `POST /api/locations/{id}/image`, `POST /api/staff/bulk/locations`.
- Removed fields: `locations` on register and update staff bodies, `LocationIds` on `StaffDetailDto`.
- Removed permission key `locations.manage` (permission catalog and role/user permission responses).
- FE and E2E change in the same PR; no deprecation window (single-tenant, deployed together).

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Irreversible loss of locations + staff assignments | High | Med | Backup; optional CSV export of both tables before the drop |
| Orphan `locations.manage` rows in role/user permission tables break permission pages | Med | Med | Delete the grants in the migration (precedent: `CleanStaleFeatureFlags`); test the permission screens |
| Staff tests/E2E still create staff with `locations` | High | Low | Grep after each layer; `negative.spec`/`workflow.spec` still create locations, fix them |
| Stale docs (README still describes log-shift, reports, locations) | High | Low | Include README rewrite of the affected rows; Phase 7 did not touch README |
| Uploaded location images left on the server volume | Med | Low | See open question 1 |

---

## Implementation Steps

Leaf code first, schema last (every commit builds). Branch `feat/remove-locations` cut from master after Phase 7 merges.

1. Re-measure on the new master (`git grep`), confirm the counts above — S
2. `[TDD]` Frontend: add a guard test/grep that no `Location` symbol remains in `projects/`; delete the Locations page, route, nav, `LocationStore`, model, exports; remove the multiselects, bulk action and `locationIds` from staff pages, service, store and models; remove i18n keys (en/zh/vi, line-based edits, pure deletions); trim specs — M
3. `[TDD]` Backend application/API: remove `LocationsController`, `Application/Locations`, `BulkAssignLocationsCommand`, `Locations` params and `LocationIds`, `locations.manage` constant/seed; fix `StaffController` bodies; update `BulkCommandTests`, `StaffDeleteCascadeTests`, `TestDbContext` — M
4. Domain/persistence: delete `Location`, `StaffLocation`, configurations, DbSets, `Staff` navigation/methods, seed rows — S
5. E2E and scripts: delete `locations.spec.ts`; trim `staff.spec`, `negative.spec`, `workflow.spec` (no location fixtures); trim `seed-and-demo-hardening.ps1` — S
6. Docs: CLAUDE.md (structure, stores list, routes, "Staff ↔ location assignment", bulk text, E2E table), README (routes table, assignment section, bulk text) — S
7. **Ask the user**, then generate `DropLocations` (drops `StaffLocations` then `Locations`, deletes `locations.manage` grants); apply to a copy of prod data first — S
8. Full verification, PR (squash), manual deploy workflow in a maintenance window with a fresh backup — S

---

## Interaction with other migrations

- Phase 7 `DropWorkLogs` and this `DropLocations` touch different tables with no FK between them, so they can be separate migration files in **one** maintenance window if both PRs are merged before that deploy. Keep them separate files so either can be reverted alone.
- Phase 7i (English only) later drops `Roles.NameZh/NameVi` and the language columns; the Location columns will already be gone, so 7i gets smaller.

---

## Effort

About 2 days after Phase 7 merges: frontend 0.5-1, backend 0.5, E2E/docs/verification 0.5. Steps 2-3 carry most of the risk.

---

## Validation Criteria

- [ ] `grep -ri "location"` over `projects/`, `backend/src`, `backend/tests`, `tests/e2e` returns only `window.location`, `Set-Location`, nginx `location` and migrations
- [ ] `npx ng test shared-lib|admin-app|staff-app --no-watch` green; counts drop only by removed specs
- [ ] `dotnet test` green (Application, Infrastructure; Integration compiles and passes where Docker is available)
- [ ] `npx ng build` clean for both apps; coverage scan (Rule 17) returns 0 spec gaps
- [ ] E2E suite green against the rebuilt Docker stack
- [ ] `DropLocations` applies cleanly on a copy of prod data; permission pages load
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge commit.
- **Data:** restore from the pre-migration backup (`scripts/restore-db.sh`, destructive, prompts YES). Without the backup the locations and assignments cannot be recovered.

---

## Open questions

1. Delete the uploaded location images under `/uploads/locations/` on the server, or leave them orphaned?
2. Export `Locations` + `StaffLocations` to CSV before the drop, or is losing them fine?
3. Should anything replace "assigned work location" (for example a department field in a later HRM phase), or is nothing needed?

---

## Review Notes

_Filled in after the phase completes._

| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] Phase 7i plan updated (Location columns gone)
- [ ] CLAUDE.md and README updated
