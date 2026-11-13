# HRNexus: HR Management System

Internal HR management for venue staff and managers: staff records, roles and permissions, leave, and
notifications. Two Angular PWAs (a desktop-first admin app and a mobile-first staff app) on one ASP.NET Core API.

**Stack:** Angular 21 (standalone components, signals) · PrimeNG 21 · ASP.NET Core 10 · Entity Framework Core · SignalR · PostgreSQL 17 · Docker + nginx

Direction: clock-in/out was removed (Phase 7); the product is being built out into a standard HRM
(payroll, documents and compliance, onboarding, recruitment, performance reviews). The roadmap is in
[docs/process](docs/process).

## Layout

| Path | What |
|---|---|
| `backend/src/Domain` | Entities with private setters and behaviour, constants |
| `backend/src/Application` | Use cases (commands and queries), validators, interfaces |
| `backend/src/Infrastructure` | EF Core + migrations, SignalR hub, background jobs, auth, services |
| `backend/src/Api` | REST controllers, middleware, composition root |
| `backend/tests` | Application (EF InMemory), Infrastructure and Integration (Testcontainers) tests |
| `projects/admin-app` | Manager and admin PWA (desktop-first) |
| `projects/staff-app` | Staff PWA (mobile-first) |
| `projects/shared-lib` | Angular library shared by both apps: models, services, stores, guards, components |
| `tests/e2e` | Playwright end-to-end tests |
| `nginx/` | Reverse proxy configuration (Docker) |
| `docs/` | Process phases, team protocol, deployment guide |

Development patterns and conventions: [CLAUDE.md](CLAUDE.md). Current state and resume pointer: [STATE.md](STATE.md).

## Features

### Admin app

| Route | Feature | Gate |
|---|---|---|
| `/dashboard` | Total-staff KPI with live SignalR connection status | `portal.admin` |
| `/staff`, `/staff/:id` | Staff list, register, edit, deactivate, reset password, roles, locations, per-user permission overrides, bulk actions | `staff.view` |
| `/leave` | Leave requests: approve, reject, create, edit, balances, bulk actions | `leave.approve` |
| `/roles` | Role management and per-role default permissions | `roles.manage` |
| `/locations` | Location management | `locations.manage` |
| `/payroll-locks` | Payroll month locks (no enforcement yet; redefined in the payroll rebuild) | `payroll.lock` |
| `/cash-advances` | Salary advance records | `cashadvance.manage` |
| `/account` | Own profile, change password, push notifications | none |

### Staff app

| Route | Feature |
|---|---|
| `/home` | Greeting and leave balances |
| `/my-leave` | Submit leave, view history and balance |
| `/profile` | Personal info, change password, push notifications, language |

## Run it locally

Prerequisites: Docker, or Node 22 + .NET 10 SDK + PostgreSQL 17 for running without Docker.

```bash
# Full stack
cp .env.example .env              # fill in the values (never commit .env)
docker compose build --no-cache && docker compose up -d
```

| App | URL |
|---|---|
| Admin app | http://localhost:8080 |
| Staff app | http://localhost:8081 |

Use different hostnames for the two apps in one browser (`admin.localhost:8080`, `staff.localhost:8081`),
because the auth cookie is host-only and a login on one app would replace the other's. After any deploy,
hard-refresh the browser (**Ctrl+Shift+R**).

```bash
# Without Docker (API running separately; proxy.conf.json routes /api to it)
npx ng build shared-lib           # build the shared library first
npx ng serve admin-app            # http://localhost:4200
npx ng serve staff-app            # http://localhost:4201
```

Seeded development accounts are created by `SeedData` on first start. They exist for local databases only:
change or remove them before exposing any instance, and see [docs/DEPLOY.md](docs/DEPLOY.md) for production.

## Test

```bash
npx ng test shared-lib --no-watch
npx ng test admin-app  --no-watch
npx ng test staff-app  --no-watch

cd backend
dotnet test tests/Application.Tests/         # fast, EF InMemory
dotnet test tests/Infrastructure.Tests/
dotnet test tests/Integration.Tests/         # needs Docker (Testcontainers)

npm run test:e2e                             # Playwright against the running Docker stack
```

## Git workflow and CI/CD

Feature branch, then pull request, then squash-merge into `master`. Direct pushes to `master` are blocked by a
branch ruleset. The full flow, branch and commit naming, git hooks, required checks and the manual production
deploy are in [docs/process/git-workflow.md](docs/process/git-workflow.md).

| Workflow | When | Does |
|---|---|---|
| `ci.yml` | push, PR | web lint/test/build, API build + unit + integration tests, dependency audit |
| `pr-guard.yml` | PR | conventional PR title, branch naming |
| `e2e.yml` | manual, scheduled | Playwright suite against the compose stack |
| `codeql.yml` | PR, weekly | CodeQL for C# and TypeScript |
| `dast.yml` | scheduled, manual | OWASP ZAP baseline scan |
| `release.yml` | tag `v*` | GitHub release |
| `deploy.yml` | manual only | environment-gated deploy to the production server |

## Auth and permissions

| Role | Portals | Notes |
|---|---|---|
| `admin` (SuperAdmin) | Both | Full permissions by default |
| `manager` | Both | Full permissions by default |
| `employee` | Staff only | Own data only |

Access is controlled by a permission framework, not by roles directly. Each role carries default permissions and
each user can have per-user overrides (grant or revoke). Admin-portal access requires `portal.admin`. Permissions
are resolved server-side per request (cached, invalidated on change), so a revocation takes effect immediately.

- **Sessions:** httpOnly cookies (`SameSite=Strict`, `Secure` over HTTPS). Tokens are never exposed to JavaScript.
- **Login hardening:** an account locks for 15 minutes after 5 consecutive failed logins, plus a per-client-IP rate limiter on `/api/auth/*`.
- **PII at rest:** IC, passport, phone and date of birth are encrypted (AES-256-GCM) with a key from the environment.

## i18n

The UI currently ships English, Simplified Chinese and Vietnamese (`projects/*/public/i18n/{en,zh,vi}.json`).
Multi-language support is being removed in favour of English only (see [docs/process](docs/process)).

## Team

Delivery roles (product, BA, design, architecture, QA personas, testers) are defined as agents in `.claude/agents/`;
the hand-off protocol is in [docs/team/PROTOCOL.md](docs/team/PROTOCOL.md).
