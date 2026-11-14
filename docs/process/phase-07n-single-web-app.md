# Phase 7n: One web app, controlled by role and access right

> **Status:** `Implemented` (7n-a #108, 7n-b #110, 7n-c cleanup in the PR after it); review and sign-off pending
> **Created:** 2026-11-14 | **Updated:** 2026-11-14
> **Depends on:** Phases 7l and 7m-a merged. **Comes before** 7m-b (policy page) and every Phase 8 screen: all new frontend work targets the single app.
> **Why:** the HRM does not need two web apps (owner, 13 Nov 2026: "focus on one and control by role and access right"). Two apps duplicate the shell, login, PWA and tests, forced the two-hostname cookie workaround, and do not fit a product that serves companies with different roles. The permission system (roles, per-user overrides, guards) already decides what each person may do, so one app can show each person only what they are allowed to see.

---

## Objectives

1. **One web app, one URL, one login.** Every signed-in user lands in the same app; the menu and the pages they can open depend only on their permissions.
2. **Self-service pages for everyone** (home, my leave, profile, later my payslips): they need only a signed-in, active account.
3. **Management pages gated by permission keys**, exactly as today (`staff.view`, `leave.approve`, `roles.manage`, `payroll.view`, `policy.manage`, ...). A user with no management permission simply sees the self-service menu.
4. The `portal` login split and the `portal.admin` permission are **retired**; the "last administrator" lock-out guard moves to the permission that really matters (`roles.manage`).
5. The second app is gone: no `staff-app` project, no port 8081, no staff E2E project, no two-hostname caveat; one nginx site, one PWA, one build.
6. **Mobile and desktop both work** in the one app (responsive shell), so staff on phones keep the experience the staff app gave them.
7. Extras found by the 13 Nov audit are removed in the same phase (list below), so the single app starts clean.

---

## Verified starting point

| Area           | Today                                                                                                                                                                                                                              |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Apps           | `projects/admin-app` (desktop, 8080) and `projects/staff-app` (mobile, 8081) plus `shared-lib`; both have their own shell, login, forgot-password, PWA config, i18n-free pages and spec files (staff-app: 6 spec files, 138 tests) |
| Login          | `LoginCommand` takes `portal` (`admin` needs `portal.admin`; `staff` blocks admin-only accounts)                                                                                                                                   |
| Lock-out guard | `PortalAdminInvariant` stops an edit that would leave no active user with `portal.admin`                                                                                                                                           |
| Infra          | `nginx.conf` has two server blocks (8080, 8081); Dockerfile builds both; compose publishes both; CI builds and tests both; DAST scans both; Playwright has `admin` and `staff` projects                                            |
| Cookies        | host-only auth cookie, so the two portals needed `admin.localhost` and `staff.localhost`                                                                                                                                           |

---

## Design

- **Project:** `projects/admin-app` becomes **`projects/web`** (`ng` project `web`), absorbing the staff pages. `shared-lib` stays.
- **Routes:** `/auth/**` public. Signed-in shell with: `/home` (self-service landing; shows the manager dashboard cards when the user holds the matching permissions), `/my-leave`, `/profile`, then the existing management routes behind `permissionGuard`. Landing redirect: everyone goes to `/home`.
- **Shell:** one responsive shell. Desktop: collapsible sidebar. Phone: the same menu as a drawer plus the staff app's compact top bar. Menu items are filtered by permission; a "My work" group (home, my leave, profile) is always shown. Keeps the notification bell, theme toggle, SignalR connection, push opt-in, pull-to-refresh and the service-worker update check.
- **Backend:** `LoginCommand` loses `portal`; any active account may sign in. `portal.admin` is removed from the permission catalog, seeds and UI (a migration deletes its grants). The lock-out invariant becomes "at least one active user can manage roles" (`roles.manage`).
- **Access model unchanged otherwise:** roles carry default permissions, per-user overrides adjust them, the server enforces every endpoint. The frontend only hides what the server would refuse.
- **Infra:** one nginx server on 8080 (both the API proxy and `/hubs/`), one image, one `dist`, compose publishes 8080 only.
- **Tests:** the staff-app specs move into `web` (login and forgot-password duplicates are dropped, the three page specs and the shell behaviours are merged). E2E has one project; the staff specs run in it as an ordinary employee account.

---

## Scope

### In scope

- Backend: remove `portal` and `portal.admin`, re-point the lock-out guard, migration, tests.
- Frontend: rename to `web`, move `home`, `my-leave`, `profile`, merge the shells, permission-filtered menu, delete `staff-app`.
- Infra and docs: nginx, Dockerfile, compose, CI, DAST, Playwright, `package.json` scripts, CLAUDE.md, README, templates.
- The cleanup list below.

### Cleanup folded in (from the 13 Nov audit; each verified as having zero references)

- Legacy mockups `docs/hostess-*.html`.
- Unused backend constants (`Roles.ManagerOrAdminClaim`, `Roles.Employee`, unused `Limits` entries after checking `PasswordPolicy`), unused `IFileStorage` / `LocalFileStorage` and the whole uploads plumbing (`wwwroot/uploads`, compose volume, nginx `/uploads/`, proxy entries). Phase 9 (documents) re-adds storage when it has a real consumer.
- Unused shared-lib directives (`PickerOnlyDirective`, `HasPermissionDirective`) and their docs, the dead `.bulk-grid` styles, stale `ngsw` routes (`/api/roster`, `/api/announcements`), the notification types `message` and `announcement`, stale comments and user-facing copy about removed features (clock-out reminders, shift logs, attendance CSV, hourly rate), the one-off `tint-admin-icons.js` and its `sharp` dependency.
- Judgement calls decided in the cleanup PR: `nginx/nginx.local.conf` (keep only if used), completed phase documents (kept, they are history), `/hubs/shift` rename (separate small change).

### Out of scope (deferred)

- New screens (policy page 7m-b, payroll 8x); company branding and theming; multi-company in one installation.
- Reworking permission keys beyond retiring `portal.admin`.

---

## Risks

| Risk                                                                 | Likelihood | Impact | Mitigation                                                                                                                                                  |
| -------------------------------------------------------------------- | ---------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A staff user reaches a management page by URL                        | Low        | High   | Every management route keeps `permissionGuard`; the server still enforces every endpoint (tests: employee gets 403 and a redirect on each management route) |
| Removing `portal.admin` locks everyone out of role management        | Low        | High   | Guard re-pointed to `roles.manage` first, with its own tests; migration only deletes grants of the retired key                                              |
| Phone users lose the staff app's ergonomics                          | Med        | Med    | Reuse the staff shell's compact top bar and drawer; check at 360 px with the existing responsive table pattern                                              |
| Merged shell regresses notifications, SignalR, push or update checks | Med        | Med    | Move the specs with the code; E2E covers login, bell, navigation for both an admin and an employee                                                          |
| Big rename breaks paths (CI, Docker, docs)                           | Med        | Low    | Do the rename in its own commit with `git mv`; grep guard for `admin-app` and `staff-app` strings                                                           |
| Existing installs have users with only `portal.admin`                | Low        | Low    | Default roles already hold concrete keys; no production data exists                                                                                         |

---

## Implementation steps

**7n-a: backend (about 0.5 day)**

1. `[TDD]` Login works for any active account with or without a `portal` value (the field is ignored then removed); the "admin-only accounts cannot sign in to the staff portal" and "needs `portal.admin`" tests become "every active account can sign in".
2. `[TDD]` Re-point the lock-out guard to `roles.manage` (tests: cannot remove the last holder from a role, cannot strip your own last route to `roles.manage`).
3. Remove `portal.admin` (catalog, seed, FE catalog and labels, per-user override screen), migration deleting its grants; update permission tests.

**7n-b: frontend (about 1.5 days)** 4. `git mv projects/admin-app projects/web`, update `angular.json`, `package.json`, tsconfig paths, CI, Docker build, docs; green build and tests before anything else changes. 5. `[TDD]` Permission-filtered menu and routes; "My work" group; `/home` landing for everyone (manager cards only with permissions). 6. Move `home`, `my-leave`, `profile` (and their specs) from `staff-app`; merge the shell behaviours (top bar, drawer, bell, SignalR, push, update); responsive checks at 360 px and 1440 px. 7. Delete `projects/staff-app`, its ngsw/proxy config and styles; drop the `portal` argument from the login component and `AuthService`.

**7n-c: infrastructure, tests, docs, cleanup (about 1 day)** 8. nginx (one server), Dockerfile, compose (8080 only), CI jobs, DAST (one target), Playwright (one project; staff specs run as an employee), scripts. 9. The cleanup list; CLAUDE.md (project overview, routing, commands, cookie caveat removed, deployment), README, PR template, ADR. 10. Full verification: all suites, E2E against the rebuilt stack, manual check of an employee and an admin account at phone and desktop width.

---

## Validation criteria

- [ ] One `ng` application plus `shared-lib`; `git grep -n "staff-app"` finds only history (phase documents)
- [ ] An employee account signs in, sees only the self-service menu, and gets the redirect (and a 403 from the API) on every management route
- [ ] An admin account sees the self-service pages and every management page its permissions allow
- [ ] The lock-out guard prevents removing the last holder of `roles.manage`
- [ ] `dotnet test`, `npx ng test` (web and shared-lib), builds, ESLint, Prettier ratchet, `dotnet format` clean; E2E green with a single Playwright project
- [ ] Docker serves everything on 8080; no 8081 listener
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the sub-phase PR; the sub-phases build on each other in order, so revert in reverse order.
- **Data:** the migration deletes only the grants of the retired `portal.admin` key; reverting the code leaves those grants missing, so re-grant them from the roles page if the split were ever restored.

---

## Open questions

1. Name of the merged project: `web` (assumed). Say if you prefer another.
2. Should the landing page for managers be the current dashboard instead of `/home`? (Assumed: one `/home` that adds dashboard cards for people who hold the permissions.)
3. Should a role be able to hide self-service pages (for example no leave self-service for contractors)? (Assumed no; every account gets the self-service pages.)

---

## Decisions made while building

- The dashboard stays its own menu item and page, gated by `reports.view` (the server endpoint needs it), instead of cards on `/home`. Revisit if managers want one landing page.
- The notification bell no longer filters by portal; it shows every notification the server sent to the person.
- The Prettier ratchet judges a renamed file by the file it came from, so moving `admin-app` to `web` did not force reformatting about 100 files.
- Playwright runs one `web` project on Desktop Chrome. A phone-width project can be added later if phone-only layout bugs appear.

- Cleanup decisions: `nginx/nginx.local.conf` was referenced by nothing and is deleted; the uploads plumbing (`IFileStorage`, `LocalFileStorage`, static files, the `uploads` volume, nginx and dev-proxy `/uploads`) is deleted, and the nginx body limit drops from 10 MB to 1 MB until Phase 9 adds a real upload endpoint; completed phase documents are kept as history; the `/hubs/shift` rename is left as a separate small change.
- Finding: `PayrollLockGuard` has no caller, so locking a month freezes nothing today. The copy no longer claims otherwise; Phase 8 (payroll runs) wires the guard in.
- The ADR is `docs/architecture/adr-single-web-app.md`.

## Review Notes

_Filled in after the phase completes._

| Issue | Perspective | Severity | Affects Phase | Resolution |
| ----- | ----------- | -------- | ------------- | ---------- |

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] CLAUDE.md, README and the PR template updated; Phase 7m and 8 plans point at the single app
