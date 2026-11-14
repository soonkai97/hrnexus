# HRNexus: HR Management System

Internal HR management for venue staff and managers: staff records, roles and permissions, leave, and
notifications. One responsive Angular PWA for managers and staff, on one ASP.NET Core API. What a person sees and
can do depends on their permissions.

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
| `projects/web` | The web app (PWA) for managers and staff; access is controlled by role and permission |
| `projects/shared-lib` | Angular library used by the web app: models, services, stores, guards, components |
| `tests/e2e` | Playwright end-to-end tests |
| `nginx/` | Reverse proxy configuration (Docker) |
| `docs/` | Process phases, team protocol, deployment guide |

Development patterns and conventions: [CLAUDE.md](CLAUDE.md). Current state and resume pointer: [STATE.md](STATE.md).

## Features

Everyone signs in at the same URL and lands on `/home`. Pages are shown by permission.

### Management pages

| Route | Feature | Gate |
|---|---|---|
| `/dashboard` | Total-staff KPI with live SignalR connection status | `reports.view` |
| `/staff`, `/staff/:id` | Staff list, register, edit, deactivate, reset password, roles, per-user permission overrides, bulk actions | `staff.view` |
| `/leave` | Leave requests: approve, reject, create, edit, balances, bulk actions | `leave.approve` |
| `/roles` | Role management and per-role default permissions | `roles.manage` |
| `/payroll-locks` | Payroll month locks (no enforcement yet; redefined in the payroll rebuild) | `payroll.lock` |
| `/cash-advances` | Salary advance records | `cashadvance.manage` |

### Self-service pages (every signed-in account)

| Route | Feature |
|---|---|
| `/home` | Greeting and leave balances |
| `/my-leave` | Submit leave, view history and balance |
| `/profile` | Personal info, change password, push notifications |

## Planned

- Then the leave fixes and the payroll rebuild (phase 8), see [docs/process](docs/process).

## Run it locally

Prerequisites: Docker, or Node 22 + .NET 10 SDK + PostgreSQL 17 for running without Docker.

```bash
# Full stack
cp .env.example .env              # fill in the values (never commit .env)
docker compose build --no-cache && docker compose up -d
```

| App | URL |
|---|---|
| Web app | http://localhost:8080 |

After any deploy, hard-refresh the browser (**Ctrl+Shift+R**).

```bash
# Without Docker (API running separately; proxy.conf.json routes /api to it)
npx ng build shared-lib           # build the shared library first
npx ng serve web                  # http://localhost:4200
```

Seeded development accounts are created by `SeedData` on first start. They exist for local databases only:
change or remove them before exposing any instance.

## Test

```bash
npx ng test shared-lib --no-watch
npx ng test web        --no-watch

cd backend
dotnet test tests/Application.Tests/         # fast, EF InMemory
dotnet test tests/Infrastructure.Tests/
dotnet test tests/Integration.Tests/         # needs Docker (Testcontainers)

npm run test:e2e                             # Playwright against the running Docker stack
# PW_CHANNEL=msedge npm run test:e2e         # use the installed Edge/Chrome if Playwright's bundled Chromium cannot start
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

## Auth and permissions

| Account | Sees | Notes |
|---|---|---|
| Any signed-in account | Self-service pages (home, my leave, profile) | Own data only |
| An account whose jobs or overrides grant management permissions | Plus the matching management pages | Each page needs its own permission |
| The first administrator job (`ADMINISTRATOR` on a new install) | Everything | Holds every permission; cannot be deleted or edited, can be renamed |

No job is special because of its name. Access is controlled by a permission framework, not by roles directly. Any active account can sign in at the same
URL; each role carries default permissions and each user can have per-user overrides (grant or revoke), and those
decide which management pages and actions a person gets. Permissions are resolved server-side per request
(cached, invalidated on change), so a revocation takes effect immediately.

- **Sessions:** httpOnly cookies (`SameSite=Strict`, `Secure` over HTTPS). Tokens are never exposed to JavaScript.
- **Login hardening:** an account locks for 15 minutes after 5 consecutive failed logins, plus a per-client-IP rate limiter on `/api/auth/*`.
- **PII at rest:** IC, passport, phone and date of birth are encrypted (AES-256-GCM) with a key from the environment.

## Team

Delivery roles (product, BA, design, architecture, QA personas, testers) are defined as agents in `.claude/agents/`;
the hand-off protocol is in [docs/team/PROTOCOL.md](docs/team/PROTOCOL.md).
