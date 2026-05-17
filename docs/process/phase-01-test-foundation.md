# Phase 01: Test Foundation

> **Status:** `Complete`
> **Created:** 2026-05-17 | **Updated:** 2026-05-17
> **Depends on:** None

---

## Objectives

1. Configure working test infrastructure (Vitest via `ng test`, Playwright for E2E)
2. Achieve ≥70% coverage on `shared-lib` — the code shared by both apps
3. Write unit tests for all guards, pipes, and critical services in shared-lib
4. Create one passing E2E spec per app (auth flow) as the E2E baseline
5. Establish test conventions that all future phases follow

---

## Scope

### In scope
- Vitest configuration (`vitest.config.ts`, angular.json test targets)
- Playwright setup (`playwright.config.ts`, `tests/e2e/` structure)
- Unit tests for **shared-lib** only:
  - `AuthService` (token storage, refresh, login, logout, role checks)
  - `authGuard`, `roleGuard`, `featureFlagGuard`, `featurePinGuard`
  - `AmPmPipe`
  - `ToastService`, `ConfirmService` (basic smoke tests)
  - `StaffStore` (loadStaff, register, update — signal state changes)
  - `LangService` (current signal, set, toggle)
- E2E baseline: admin + staff auth flow specs
- npm script aliases for scoped test runs

### Out of scope (Phase 02)
- Component tests for admin-app feature pages
- Component tests for staff-app feature pages
- GitHub Actions CI pipeline
- Coverage for admin-app or staff-app app code

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| `vitest@^4.0.8` installed | npm | ✅ Done |
| `@angular/build:unit-test` builder | Angular 21 | ✅ Done |
| `@playwright/test` installed | npm | In progress |
| Docker running for E2E targets | Infra | Required before E2E |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Angular 21 TestBed setup differs from Angular 16 patterns | Med | Med | Use `TestBed.configureTestingModule()` with `importProvidersFrom` — don't use NgModule |
| Signal-based services are harder to test (no `spyOn` on signals) | High | Med | Test signals via their public API — set inputs, read outputs |
| `StaffStore` makes HTTP calls — need to mock `HttpClient` | Med | Med | Use `provideHttpClientTesting()` from `@angular/common/http/testing` |
| PrimeNG in tests may require DOM environment | Low | Low | jsdom handles basic DOM; skip PrimeNG-specific rendering assertions |
| E2E tests flaky if Docker isn't running | High | Low | Mark E2E as optional in local dev; required only in CI |

---

## Implementation Steps

### Infrastructure (no TDD needed — config files)
1. Create `vitest.config.ts` at root — S
2. Update `angular.json` test targets for admin-app and staff-app — S
3. Update `package.json` scripts — S
4. Install `@playwright/test` — S
5. Create `playwright.config.ts` — S
6. Create `tests/e2e/` skeleton (admin/auth, staff/auth, shared/helpers) — S
7. Verify: `ng test shared-lib --watch=false` passes existing 2 specs — S

### Unit tests — shared-lib (TDD)
8. `[TDD]` `AmPmPipe` — null, valid time, midnight edge cases — S
9. `[TDD]` `LangService` — initial signal value, `set()`, `toggle()`, localStorage persistence — S
10. `[TDD]` `authGuard` — authenticated redirects to requested route; unauthenticated redirects to /auth/login — M
11. `[TDD]` `roleGuard` — correct role passes; wrong role redirects — M
12. `[TDD]` `featureFlagGuard` — enabled flag passes; disabled redirects to fallback — M
13. `[TDD]` `AuthService` — `isAuthenticated()`, `userRole()`, `hasRole()`, `hasValidToken()` with expired JWT — M
14. `[TDD]` `ToastService` — emits correct severity/detail pairs — S
15. `[TDD]` `StaffStore` — signal state after `loadStaff()` with mocked HTTP — L

### E2E baseline
16. E2E: Admin auth spec passes against running Docker (unauthenticated redirect + login form visible) — M
17. E2E: Staff auth spec passes — S

---

## Validation Criteria

- [x] `npm run test:shared` exits 0 — **100 tests, 9 files, all pass**
- [x] `npm run test:coverage` — lines 72.5% ✅, branches 88.88% ✅, statements 72.22% ✅ — functions 52.52% ❌ (Phase 02 item)
- [x] `npm run test:e2e:admin` passes — **4/4 tests pass** against Docker
- [x] `npm run test:e2e:staff` passes — **3/3 tests pass** against Docker
- [ ] `ng build admin-app --configuration production` — not run (out of phase scope; infrastructure only)
- [ ] `ng build staff-app --configuration production` — not run (out of phase scope)
- [x] Multi-perspective review completed (see `REVIEW_CHECKLIST.md`) — 2026-05-17

---

## Rollback

Tests are additive — they cannot break production code. If infrastructure changes (angular.json, package.json) cause build issues, revert those specific files. Test spec files can be removed without affecting the app.

---

## Phase 02 Preview (subject to change after Phase 01 review)

Based on current plan, Phase 02 will cover:
- Component-level unit tests for admin-app critical pages (dashboard, staff list, staff detail)
- Component-level unit tests for staff-app (home, log-shift, my-leave)
- GitHub Actions CI pipeline: `ng build` + `ng test` gate on PR

---

## Review Notes

**Reviewed:** 2026-05-17 | **Result:** Conditionally Passed (2 P2 issues → Phase 02)

### Coverage results

| Metric      | Actual | Target | Status |
|-------------|--------|--------|--------|
| Statements  | 72.22% | ≥70%   | ✅     |
| Branches    | 88.88% | ≥70%   | ✅     |
| Functions   | 52.52% | ≥70%   | ❌     |
| Lines       | 72.50% | ≥70%   | ✅     |

Low function coverage is concentrated in untested paths: `AuthService` (login, refresh, forgotPassword, forceChangePassword), `FeatureFlagService` (toggle, enableAll, disableAll), `StaffStore` (createRole, deleteRole, resetPassword, deleteStaff).

### 7-Perspective Checklist

#### Perspective 1 — Architecture ✅
All items pass. Spec files are purely additive — zero production code changed.

#### Perspective 2 — Scalability ✅
All items pass. Pagination tested in `StaffStore`; store mutations return Observables.

#### Perspective 3 — Security ⚠️
- ✅ All new specs avoid logging sensitive data
- ✅ Guards verified to enforce auth/role boundaries correctly
- ⚠️ **Login form accessibility gap**: `<label>` elements are not linked to inputs via `for`/`id` — discovered when E2E `getByLabel()` selectors failed. Screen-reader users and browser autofill are affected. (See Issue #3)

#### Perspective 4 — Maintainability ⚠️
- ✅ All WHY-first test comments in place
- ✅ No magic strings, no commented-out code
- ⚠️ `makeJwt()` helper duplicated in `auth.service-token.spec.ts` and `auth.guard.spec.ts` — extract to a shared test helper in Phase 02. (See Issue #4)

#### Perspective 5 — Testing ❌ (function coverage)
- ✅ Intent-driven tests with WHY comments throughout
- ✅ Loading/error/empty states all tested in `StaffStore`
- ✅ No placeholder tests
- ❌ Function coverage 52.52% — below 70% target (see Issue #1)
- ⚠️ `featurePinGuard` listed as in-scope but no spec written (see Issue #2)
- ⚠️ `ConfirmService` listed as in-scope but no spec written (see Issue #2)
- ✅ E2E covers auth happy-path and invalid-credentials edge case for both apps

#### Perspective 6 — Product Alignment ✅
- ✅ All planned steps executed
- ✅ No scope creep
- ✅ Admin E2E at Desktop Chrome; Staff E2E at Mobile Chrome (Playwright `devices['Mobile Chrome']`)
- Note: E2E selectors needed correcting from `getByLabel()` to `getByPlaceholder()` — see Issue #3

#### Perspective 7 — Performance ✅
All items pass. Test files are excluded from production bundles; no lazy-loading changes; no regressions.

---

### Issues found

| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|
| Function coverage 52.52% — below 70% target. Uncovered: `AuthService` login/refresh/forgotPassword/forceChangePassword; `FeatureFlagService` toggle/enableAll/disableAll; `StaffStore` createRole/deleteRole/resetPassword/deleteStaff | 5 — Testing | P2 | Phase 02 | Add targeted tests for remaining functions in Phase 02 shared-lib coverage sprint |
| `featurePinGuard` and `ConfirmService` were listed in scope but have no spec files | 5 — Testing | P2 | Phase 02 | Add `feature-pin.guard.spec.ts` and `confirm.service.spec.ts` to Phase 02 scope |
| Login form inputs lack `for`/`id` label linkage — `<label>` elements not associated with inputs; breaks screen-reader UX and browser autofill heuristics | 3+6 — Security/Product | P2 | Phase 02 | Add `id` to each input and `for` attribute to matching `<label>` in both admin and staff login components; update E2E selectors to use `getByLabel()` |
| `makeJwt()` helper duplicated in two spec files (`auth.service-token.spec.ts`, `auth.guard.spec.ts`) | 4 — Maintainability | P3 | Phase 02 | Extract to `tests/helpers/jwt.ts` or `shared-lib/src/testing/index.ts` |

---

### Plan updates

**Phase 02 scope additions (from this review):**
1. Add `feature-pin.guard.spec.ts` to Phase 02 unit test scope
2. Add `confirm.service.spec.ts` to Phase 02 unit test scope
3. Add coverage sprint for untested `AuthService`, `FeatureFlagService`, and `StaffStore` methods — target ≥70% function coverage
4. Fix login form `for`/`id` label linkage (admin-app + staff-app login components) — update E2E to use `getByLabel()` after fix
5. Extract `makeJwt()` to a shared test helper module

**Phase 02 risk update:**
- Function coverage gap is larger than expected (17 percentage points below target). Phase 02 should reserve time for coverage improvement before moving to component tests.
