# Phase 05: Remaining Component Tests (Round 2)

> **Status:** `Complete`
> **Created:** 2026-05-17 | **Updated:** 2026-05-17
> **Depends on:** Phase 04 (Remaining Component Tests)

---

## Objectives

1. Add unit tests for the 4 remaining admin-app components with testable business logic
2. Add unit tests for `TipsHistoryComponent` (staff-app)
3. Clean up the `DecimalPipe` unused-import warning in `LeaveComponent` (Phase 04 finding)
4. Reach full first-pass coverage across all non-passthrough, non-UI-heavy components

---

## Scope

### In scope

**Admin-app components:**
- `OvertimeComponent` — save() validation (threshold > 0, multiplier ≥ 1), role wildcard logic, confirm-delete — **M**
- `RolesComponent` — addRole() empty-guard, dual edit modes (form add + inline rate), deleteRole() confirm — **S**
- `LocationsComponent` — save() create/update branching, remove() confirm, uploadImage() — **S**
- `AccountComponent` — initials computed, profile load, logout — **S**

**Staff-app components:**
- `TipsHistoryComponent` — load() date range correctness, leap-year February, year/month change — **S**

**Bug fix / cleanup:**
- Remove unused `DecimalPipe` import from `leave.component.ts` (NG8113 warning)

### Out of scope (deferred to Phase 06)
- `DashboardComponent` — SignalR effect + real-time patches; needs a live WebSocket mock; defer
- `AnnouncementsComponent`, `MessagingComponent`, `NotificationsComponent` — UI-heavy, low logic density; E2E covers the happy path

---

## Dependencies

| Dependency | Type | Status |
|---|---|--------|
| Phase 04 complete — 428 unit tests, 7 E2E pass | Code | ✅ Done |
| `OvertimeRuleDto` in `shared-lib/services/admin.service.ts` | Model | ✅ Exists |
| `LocationStore.create/update/remove/uploadImage` | Store | ✅ Exists |
| `StaffStore.createRole/updateRoleRate/deleteRole` | Store | ✅ Exists |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `OvertimeComponent` uses `TranslateService` for menu labels — may need mock | Med | Low | Provide `provideTranslateService` as in all other specs |
| `RolesComponent` `roles` and `loading` are `StaffStore` readonly signals — plain vi.fn() mocks | Low | Low | Same pattern as StaffComponent |
| `LocationStore` signals same pattern as `StaffStore` | Low | Low | Mock with vi.fn() returning [] |

---

## Implementation Steps

1. Fix: Remove unused `DecimalPipe` from `leave.component.ts` — XS
2. `[TDD]` `OvertimeComponent` — save() validation, role wildcard/comma logic, openEdit() parsing, delete() confirm — M
3. `[TDD]` `RolesComponent` — addRole() guard, saveRate(), deleteRole() confirm — S
4. `[TDD]` `LocationsComponent` — save() create vs update, remove() confirm, uploadImage() — S
5. `[TDD]` `AccountComponent` — initials computed variants, ngOnInit profile load, logout — S
6. `[TDD]` `TipsHistoryComponent` — load() date params, leap-year Feb, loading flag — S
7. Run `npm run test:admin` and `npm run test:staff` — all pass
8. Run `npm run test:e2e` — 7/7 pass

---

## Validation Criteria

- [x] `npm run test:admin` — 264/264 pass (11 spec files), no NG8113 warning
- [x] `npm run test:staff` — 172/172 pass (6 spec files)
- [x] `npm run test:e2e` — 7/7 pass
- [x] OvertimeComponent: threshold ≤ 0 blocked; multiplier < 1 blocked; '*' wildcard tested
- [x] RolesComponent: empty name skips API; inline rate edit clears on success
- [x] LocationsComponent: create path vs update path distinguished
- [x] AccountComponent: null user → empty initials; two-word name → two letters
- [x] TipsHistoryComponent: Feb 2024 (leap) → 29; Feb 2025 (non-leap) → 28

---

## Rollback

Tests and the DecimalPipe fix are independently revertible. `git revert` any individual commit.

---

## Review Notes

### Final counts

| Suite | Spec files | Tests |
|-------|-----------|-------|
| shared-lib | 6 | 88 |
| admin-app | 11 | 264 |
| staff-app | 6 | 172 |
| **Total** | **23** | **524** |

### Multi-perspective review

| Perspective | Finding | Severity |
|---|---|---|
| **Architecture** | All 5 new specs follow the established TestBed pattern. `LocationStore` and `StaffStore` mocked as plain vi.fn() objects — consistent with Phase 03-04. | ✅ None |
| **Scalability** | `TipsHistoryComponent` date-range logic is inline in `load()` (not extracted to computed signals like `PaySummaryComponent`). Tests capture args via `mock.calls.at(-1)` — works but tightly coupled to call count. Low risk since the component is small. | ✅ None |
| **Security** | No sensitive data in test fixtures. Image file mocked with minimal `File` constructor. | ✅ None |
| **Maintainability** | WHY comments in all 5 spec files link tests to concrete business risks (wage-theft risk, QR clock-in breakage, avatar badge, double-save). | ✅ None |
| **Testing** | `OvertimeComponent` covers both wildcard and comma-join paths — the two ways role targeting works. `LocationsComponent` tests the create/update branch split explicitly. `RolesComponent` tests both dual edit modes (form + inline). | ✅ None |
| **Product Alignment** | Wage-protection guard (multiplier < 1), location name integrity (blank name guard), role editing idempotence — all map to operational risks identified in the README. | ✅ None |
| **Performance** | All tests synchronous; no timers. DecimalPipe removal reduces admin-app bundle by one unused tree-shaken pipe. | ✅ None |

### Issues found
| Issue | Perspective | Severity | Affects Phase | Resolution |
|---|---|---|---|---|
| None | — | — | — | — |

### Plan updates
Phase 06 candidates: `DashboardComponent` (SignalR), `AnnouncementsComponent`, `MessagingComponent`, `NotificationsComponent` — all deferred as UI-heavy or requiring WebSocket mocking.
