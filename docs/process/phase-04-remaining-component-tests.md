# Phase 04: Remaining Component Tests

> **Status:** `Complete`
> **Created:** 2026-05-17 | **Updated:** 2026-05-17
> **Depends on:** Phase 03 (Component-Level Tests)

---

## Objectives

1. Add component-level unit tests for the 4 remaining admin-app feature pages
2. Add component-level unit tests for the 2 remaining staff-app feature pages
3. Fix the dead-code time-validation bug in `LogShiftComponent.submitManual()` (Phase 03 issue)
4. Achieve full component test coverage across both apps (no untested pages)

---

## Scope

### In scope

**Admin-app components:**
- `LeaveComponent` — admin leave CRUD: date-range validation, quick status, overdue filter, entitlement management — **L**
- `PayrollComponent` — pay period load, CSV export — **S**
- `TipsComponent` — distribution validation (50k cap), location lookup, preview flow — **M**
- `ReportsComponent` — 366-day date guard, preset date arithmetic, multi-tab CSV export — **M**

**Staff-app components:**
- `PaySummaryComponent` — periodStart/periodEnd computed dates, load, print — **S**
- `ProfileComponent` — `initials` computed, push notification subscribe/unsubscribe flow — **M**

**Bug fix:**
- `LogShiftComponent.submitManual()` — fix `>` → `>=` in time duration formula so same-time gives 0 (not 1440); update spec to cover both checks

### Out of scope (deferred)
- `AuthService.updateProfile()` test — function coverage still ≥90%; deferred to Phase 05
- `AnnouncementsComponent`, `MessagingComponent`, `NotificationsComponent` (admin + staff) — UI-heavy, low logic density; covered adequately by E2E → Phase 05
- `DashboardComponent`, `AccountComponent`, `FeatureFlagsComponent`, `LocationsComponent`, `RolesComponent`, `OvertimeComponent` — mostly passthrough/display; defer to Phase 05

---

## Test pattern (same as Phase 03)

```typescript
TestBed.configureTestingModule({
  imports: [ComponentUnderTest],
  providers: [
    provideHttpClient(),
    provideRouter([]),
    provideTranslateService({ defaultLanguage: 'en' }),
    { provide: SomeService, useValue: mockSomeService },
    MessageService,
    ConfirmationService,
  ],
});
const component = TestBed.createComponent(ComponentUnderTest).componentInstance;
// Do NOT call detectChanges()
```

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Phase 03 complete — 328 unit tests, 7 E2E pass | Code | ✅ Done |
| Shared test helpers in `testing/test-helpers.ts` | Code | ✅ Done |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| `LeaveComponent` has complex pagination + multi-tab state — harder to mock cleanly | Med | Med | Focus on business-logic methods; skip pagination UI interactions |
| `ProfileComponent` uses `navigator.serviceWorker` (browser API) — may not be available in jsdom | Med | Med | Guard with `typeof navigator.serviceWorker !== 'undefined'` check; mock in tests |
| `ReportsComponent` CSV formatting is tab-dependent — need to cover all 5 tabs | Med | Med | Use parameterised test cases per tab |
| LogShiftComponent formula fix may introduce regression in overnight-shift handling | Low | Med | Run `npm run test:staff` after fix; verify overnight test still passes |

---

## Implementation Steps

### Bug fix
1. Fix `LogShiftComponent.submitManual()` time formula: change `endMins > startMins` → `endMins >= startMins` so same-time correctly gives duration 0; update spec to add same-time error test — S

### Admin-app [TDD]
2. `[TDD]` `PayrollComponent` — load(), exportCsv() blob, isCurrentOrFutureMonth computed — S
3. `[TDD]` `TipsComponent` — distribute() validation (amount cap), onLocationChange(), confirm/cancel flow — M
4. `[TDD]` `ReportsComponent` — runReport() date guard, setPreset() arithmetic, downloadReportCsv() per tab — M
5. `[TDD]` `LeaveComponent` — save() date validation, quickStatus(), countByType(), loadOverdue() — L

### Staff-app [TDD]
6. `[TDD]` `PaySummaryComponent` — periodStart/periodEnd computed, load(), year/month change — S
7. `[TDD]` `ProfileComponent` — initials computed, togglePush() subscribe/unsubscribe/error — M

### Validation
8. Run `npm run test:staff` and `npm run test:admin` — verify all pass
9. Run `npm run test:e2e` — verify no regressions

---

## Validation Criteria

- [x] `npm run test:staff` — 157/157 pass (5 spec files)
- [x] `npm run test:admin` — 183/183 pass (7 spec files)
- [x] `npm run test:e2e` — 7/7 pass
- [x] LogShiftComponent same-time validation now correctly sets formError
- [x] LeaveComponent: save() validation + quickStatus() tested
- [x] ReportsComponent: 366-day guard + at least 2 preset types tested
- [x] ProfileComponent: push subscribe AND unsubscribe paths tested
- [x] Multi-perspective review completed

---

## Rollback

Tests are additive — removing spec files reverts without affecting production.
Bug fix: `git revert` the formula-change commit.

---

## Review Notes

### Final counts

| Suite | Spec files | Tests |
|-------|-----------|-------|
| shared-lib | 6 | 88 |
| admin-app | 7 | 183 |
| staff-app | 5 | 157 |
| **Total** | **18** | **428** |

### Multi-perspective review

| Perspective | Finding | Severity |
|-------------|---------|----------|
| **Architecture** | All specs follow the same TestBed pattern — no NgModules, no detectChanges. Service mocks are consistent across all 6 new files. | ✅ None |
| **Scalability** | Computed signals backed by real Angular signals (PaySummaryComponent) re-evaluate correctly on `.set()`; no caching issues. Mock-backed computed signals (HomeComponent feature flags) use factory pattern to avoid stale cache. | ✅ None |
| **Security** | No sensitive data in specs. Mocked auth tokens are obviously fake. | ✅ None |
| **Maintainability** | `makePushSub()` factory in ProfileComponent spec avoids duplication across 3 describe blocks. WHY comments explain business rationale (50k cap, 366-day guard, initials avatar). | ✅ None |
| **Testing** | ProfileComponent `togglePush()` tests cover subscribe, unsubscribe, no-key, permission-denied, and exception paths — full branch coverage. PaySummaryComponent covers leap year, non-leap, and all month-end boundaries. | ✅ None |
| **Product Alignment** | 366-day guard (ReportsComponent), 50k cap (TipsComponent), same-time duration = 0 bug fix (LogShiftComponent) all map directly to identified business risks. | ✅ None |
| **Performance** | All tests synchronous or use `await` on async togglePush. No `setTimeout`/`sleep`. navigator.serviceWorker stubs cleaned up in afterEach. | ✅ None |

### Issues found
| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|
| `DecimalPipe` imported but unused in LeaveComponent (NG8113 warning) | Maintainability | Low | Phase 05 | Remove unused import from `leave.component.ts` |

### Plan updates
Phase 05 should include: clean up the `DecimalPipe` unused-import warning in `LeaveComponent`.
