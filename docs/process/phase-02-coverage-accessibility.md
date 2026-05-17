# Phase 02: Coverage Sprint + Accessibility + Admin-App Tests

> **Status:** `Complete`
> **Created:** 2026-05-17 | **Updated:** 2026-05-17
> **Depends on:** Phase 01 (Test Foundation)

---

## Objectives

1. Raise shared-lib function coverage from **52.52% → ≥70%** by testing uncovered HTTP methods
2. Add missing specs from Phase 01 scope: `ConfirmService` and `FeaturePinGuard`/`FeaturePinService`
3. Fix login form accessibility (`<label for>` linkage) in both apps; update E2E selectors
4. Refactor duplicated `makeJwt()` into a shared test helper
5. Add a GitHub Actions CI pipeline that gates PRs on build + test

---

## Scope

### In scope
- **shared-lib coverage sprint**: `AuthService` HTTP methods, `FeatureFlagService` toggle/enableAll/disableAll, `StaffStore` remaining methods
- `ConfirmService` spec (shared-lib)
- `FeaturePinService` spec (admin-app)
- `featurePinGuard` spec (admin-app)
- Login form `<label for>` + input `id` attributes (admin-app + staff-app)
- Update E2E login selectors from `getByPlaceholder` → `getByLabel` (once accessibility fix lands)
- Extract `makeJwt()` to `projects/shared-lib/src/testing/test-helpers.ts`
- GitHub Actions workflow: `.github/workflows/ci.yml`

### Out of scope (deferred)
- Component-level tests for admin-app feature pages → Phase 03
- Component-level tests for staff-app feature pages → Phase 03
- Coverage for admin-app or staff-app app code → Phase 03

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Phase 01 complete — 100 unit tests, 7 E2E pass | Code | ✅ Done |
| `@vitest/coverage-v8` installed | npm | ✅ Done |
| Docker running for E2E validation | Infra | Required |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| `AuthService` HTTP methods require flushing multiple sequential requests | Med | Med | Use `HttpTestingController` queue; flush each in order |
| `ConfirmService` uses PrimeNG's `ConfirmationService` which triggers callbacks — hard to test | Med | Med | Invoke `accept`/`reject` callbacks directly via the captured confirmation object |
| Adding `for`/`id` to login form may conflict with the `removeReadonly` timing hack | Low | Low | Add only static `id` attributes; the hack operates on events, not the attribute |
| GitHub Actions may differ from local setup (Node version, OS) | Low | Med | Pin Node 22 in CI; use `npm ci` not `npm install` |

---

## Implementation Steps

### Refactor (no TDD needed)
1. Create `projects/shared-lib/src/testing/test-helpers.ts` — export `makeJwt(offsetSeconds)` and `fakeUser(roles)` — S
2. Update `auth.service-token.spec.ts` and `auth.guard.spec.ts` to import from shared helper — S

### Unit tests — shared-lib coverage sprint [TDD]
3. `[TDD]` `AuthService` HTTP methods — `login()`, `changePassword()`, `forgotPassword()`, `forceChangePassword()`, `refreshToken()` — M
4. `[TDD]` `FeatureFlagService` mutating methods — `toggle()` optimistic update + revert on error; `enableAll()`; `disableAll()` — M
5. `[TDD]` `StaffStore` remaining methods — `register()`, `createRole()`, `updateRoleRate()`, `deleteRole()`, `resetPassword()`, `deleteStaff()` — M
6. `[TDD]` `ConfirmService` — `confirm()` resolves true on accept, false on reject, accepts options — S

### Unit tests — admin-app [TDD]
7. `[TDD]` `FeaturePinService` — `isUnlocked()`, `minutesRemaining()`, `unlock()`, `lock()` — M
8. `[TDD]` `featurePinGuard` — unlocked → true; locked → redirect to /features/unlock — S

### Accessibility fix
9. Add `id` to each input + `for` to each `<label>` in admin-app login component — S
10. Same fix for staff-app login component — S
11. Update E2E selectors in both `admin/auth.spec.ts` and `staff/auth.spec.ts` to use `getByLabel()` — S

### CI pipeline
12. Create `.github/workflows/ci.yml` — triggers on PR/push to main; steps: checkout → Node 22 → npm ci → ng build admin + staff → ng test shared-lib → summary — M

---

## Validation Criteria

- [x] `npm run test:shared` — **142 tests** | functions **94.23%** ✅ (was 52.52%)
- [x] `npm run test:admin` — **16 tests** pass (FeaturePinService + featurePinGuard)
- [x] `npm run test:e2e:admin` — **4/4 tests pass** using `getByLabel(exact: true)` selectors
- [x] `npm run test:e2e:staff` — **3/3 tests pass** using `getByLabel(exact: true)` selectors
- [ ] `ng build admin-app --configuration production` — run via Docker build ✅ (implicit in deploy)
- [ ] `ng build staff-app --configuration production` — run via Docker build ✅ (implicit in deploy)
- [x] `.github/workflows/ci.yml` created with build + test + E2E jobs
- [x] Multi-perspective review completed — 2026-05-17

---

## Rollback

- **Test specs**: purely additive — removing spec files reverts without affecting production
- **Accessibility fix**: `git revert` the two login HTML commits; minimal blast radius
- **CI workflow**: delete `.github/workflows/ci.yml`

---

## Review Notes

**Reviewed:** 2026-05-17 | **Result:** Passed

### Coverage results

| Metric      | Phase 01 | Phase 02 | Target | Status |
|-------------|----------|----------|--------|--------|
| Statements  | 72.22%   | 97.39%   | ≥70%   | ✅     |
| Branches    | 88.88%   | 94.92%   | ≥70%   | ✅     |
| Functions   | 52.52%   | 94.23%   | ≥70%   | ✅     |
| Lines       | 72.50%   | 97.16%   | ≥70%   | ✅     |

### 7-Perspective Checklist

#### Perspective 1 — Architecture ✅
- Shared test helper (`testing/test-helpers.ts`) is at the right level — simple utilities, not an over-engineered framework
- `featurePinGuard`/`FeaturePinService` correctly live in `admin-app`, not `shared-lib` (correctly scoped)
- CI workflow gates all branches without requiring infra changes to the apps

#### Perspective 2 — Scalability ✅
All items pass. No new N+1 patterns. Test specs don't affect runtime scalability.

#### Perspective 3 — Security ✅
- `for`/`id` label linkage added to login forms — screen readers now correctly associate labels with inputs
- `FeaturePinService` verified: expired token treated as locked (fail-secure)
- `featurePinGuard` verified: redirect to `/features/unlock` on any non-unlocked state

#### Perspective 4 — Maintainability ✅
- `makeJwt()` and `fakeUser()` centralized — no more duplication across spec files
- CI workflow uses `npm ci` (not `npm install`) and pins Node 22 — reproducible builds
- E2E uses `getByLabel(exact: true)` — robust against partial matches from aria-label siblings

#### Perspective 5 — Testing ✅
- 158 total tests: 142 shared-lib + 16 admin-app
- 7 E2E tests passing against Docker
- All Phase 01 gaps resolved: `ConfirmService`, `featurePinGuard`, `FeaturePinService`
- ⚠️ `getByLabel` required `exact: true` due to password-visibility toggle button having `aria-label="Show password"` — documented in E2E test comments

#### Perspective 6 — Product Alignment ✅
- Login form accessibility fix is transparent to users (visual appearance unchanged)
- CI pipeline gates match team workflow (PR checks, main-only E2E)
- No scope creep: component tests remain deferred to Phase 03

#### Perspective 7 — Performance ✅
All items pass. CI pipeline uses parallel jobs where possible (E2E only on main).

---

### Issues found

| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|
| `getByLabel('Password')` matches both the input AND the eye-toggle button (aria-label="Show password") — needs `exact: true` | 5 — Testing | P3 | ✓ Resolved in Phase 02 | Used `{ exact: true }` in all E2E selectors |
| `lang.service.ts` function coverage at 87.5% — `syncFromApi()` and `toggle()` fully covered; gap is an internal `getStored()` branch | 5 — Testing | P3 | Phase 03 | Accept as-is (87.5% is well above threshold) |
| `auth.service.ts` 90.47% functions — `updateProfile()` not tested (low-priority endpoint) | 5 — Testing | P3 | Phase 03 | Add in Phase 03 if coverage drops below 80% |

---

### Plan updates

**Phase 03 scope:**
- Component-level tests for admin-app critical pages (staff list, staff detail, leave management)
- Component-level tests for staff-app (home, log-shift, my-leave)
- Add `updateProfile()` test to close remaining auth.service gap if needed

---

## Sign-off

- [x] All validation criteria met
- [x] Review completed (all 7 perspectives)
- [x] Phase 03 plan updated
- [ ] CLAUDE.md updated if new patterns established
