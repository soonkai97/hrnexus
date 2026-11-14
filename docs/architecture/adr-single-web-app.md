# ADR: One web app, controlled by role and access right

> **Status:** Accepted
> **Date:** 2026-11-14
> **Phase:** 7n, `docs/process/phase-07n-single-web-app.md`

---

## Context

- HRNexus shipped two Angular apps: `admin-app` (desktop, managers) and `staff-app` (mobile, staff), each with its own shell, login, forgot-password page, PWA configuration, nginx server block, CI build, DAST scan, Playwright project and spec files.
- The split decided who could sign in where: the login took a `portal` value, and the `portal.admin` permission gated the admin app.
- The auth cookie is host-only, so the two apps needed two hostnames in one browser or the last login silently overwrote the other's session.
- The product serves companies with different roles. A fixed manager/staff split fits that badly, while the permission system (roles with default permissions, per-user overrides, `permissionGuard`, server-enforced policies) already decides what each person may do.

## Decision

There is **one web app**, `projects/web`, plus `projects/shared-lib`. Every active account signs in at the same URL.

- Self-service pages (`/home`, `/my-leave`, `/profile`) need only a signed-in account.
- Management pages sit behind the permission key the server also enforces (`reports.view`, `staff.view`, `leave.approve`, `roles.manage`, `payroll.lock`, `cashadvance.manage`). A route the person cannot use redirects to `/home`.
- The menu is filtered by permission, with a "My work" group shown to everyone. The frontend only hides what the server would refuse.
- The `portal` login value and the `portal.admin` permission are gone. The "last administrator" lock-out guard (`RolesManageInvariant`) now protects `roles.manage`.
- One nginx server, one image, one `dist`, one published port (8080), one Playwright project, one DAST target.

## Consequences

- One shell, one login and one PWA to maintain; no duplicated specs. Unit tests went from three projects to two.
- The two-hostname cookie caveat is gone.
- A person with no management permission sees a small app; a manager sees the same app with more in the menu. New screens (company policy, payroll) are added once.
- A phone user gets the responsive shell (drawer, compact top bar, pull to refresh) instead of a separate app. Phone-width layouts are checked by hand; the E2E suite runs at desktop width.
- The dashboard remains its own page (needs `reports.view`) rather than cards on `/home`.
- Existing installations lose any `portal.admin` grants (migration `RetirePortalAdminPermission`); default roles already hold concrete keys, so nobody loses a real capability.

## How to reverse

1. Write a new phase document and an ADR that supersedes this one.
2. Split `projects/web` by route group into two applications over `shared-lib`, give each its own hostname, and restore a login-time check (a permission, not a `portal` value).
3. Re-add per-app nginx servers, CI jobs and Playwright projects.
