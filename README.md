# HRNexus — Workforce Management System

Internal web application for managing venue staff — attendance, leave, and payroll reporting.

**Stack:** Angular 21 (standalone components, signals) · ASP.NET Core 10 · Entity Framework Core · SignalR · PostgreSQL 17

---

## Architecture

```
hrnexus/
├── backend/                         # ASP.NET Core 10 Web API
│   └── src/
│       ├── Api/Controllers/         # REST endpoints
│       ├── Domain/Entities/         # Business entities (private setters, domain methods)
│       └── Infrastructure/          # EF Core, SignalR hub
├── projects/
│   ├── shared-lib/                  # Angular library shared by both apps
│   │   └── src/lib/
│   │       ├── models/              # DTOs and interfaces
│   │       ├── services/            # HTTP services, ToastService, ConfirmService
│   │       ├── stores/              # Signal-based state (StaffStore, LocationStore)
│   │       ├── guards/              # authGuard, permissionGuard
│   │       └── components/          # Toast, ConfirmDialog, ThemeToggle, etc.
│   ├── admin-app/                   # Manager / admin dashboard — PWA (desktop-first)
│   └── staff-app/                   # Staff-facing PWA (mobile-first)
└── nginx/                           # Reverse proxy config (Docker)
```

---

## Running

```bash
# Full stack (recommended)
docker compose build --no-cache && docker compose up -d

# After frontend changes only
docker compose build web --no-cache && docker compose up -d web

# After backend changes only
docker compose build api --no-cache && docker compose up -d api
```

After any deploy, hard-refresh the browser (**Ctrl+Shift+R**).

| App | URL |
|---|---|
| Admin app | http://localhost |
| Staff app | http://localhost:8081 |

**Local dev (no Docker)** — requires the API running separately:
```bash
npx ng build shared-lib       # build shared library first
npx ng serve admin-app        # → localhost:4200
npx ng serve staff-app        # → localhost:4201
```

---

## Features

### Admin app (`/`)

| Route | Feature | Gate |
|---|---|---|
| `/dashboard` | Live KPIs (on-shift count, hours, est. cost) + SignalR activity feed | `portal.admin` |
| `/staff` | Staff list, register, edit, deactivate, reset password, assign roles + work locations | `staff.view` |
| `/staff/:id` | Staff detail — profile, work logs, per-user permission overrides, assigned locations | `staff.view` |
| `/leave` | Leave requests — approve / reject, create, edit, balance overview | `leave.approve` |
| `/reports` | Workforce attendance report (date range, by staff / location / role / date) | `reports.view` |
| `/payroll` | Monthly work-pay report (hours × hourly rate) + CSV export | `payroll.view` |
| `/payroll-locks` | Freeze a month so its shift logs can no longer be edited | `payroll.lock` |
| `/roles` | Role management + per-role default permissions | `roles.manage` |
| `/locations` | Location management (venues staff can be assigned to and clock in at) | `locations.manage` |
| `/account` | Own profile, change password, push notifications | — |

### Staff app (`/`)

| Route | Feature |
|---|---|
| `/home` | Overview dashboard — greeting, clock-in status, leave balances, recent shifts |
| `/log-shift` | Clock in / clock out, manual entry, shift history (location list limited to assigned venues) |
| `/my-leave` | Submit leave, view history and balance |
| `/profile` | Personal info, change password, push notifications, language |

---

## Pay model

Staff are **commission-based** and **record their own actual earnings outside the system**. The app
does not hold a per-staff earnings ledger.

- **Shift logs** (`/log-shift` → workforce report) — attendance tracking. Records clock-in/out times.
  The "Est. Labour Cost" in Reports and the **Payroll** report (total shift hours × the role's hourly
  rate, snapshotted on each log) are **estimates** for management, not the staff's actual payout.
- All leave is **unpaid** for commission-based staff.

Hourly rates are kept as **effective-dated history**, so a rate change never rewrites past pay estimates.

---

## Staff ↔ location assignment

Each staff member is assigned the **work location(s)** they're allowed to clock in at (many-to-many,
independent of roles). Assignment is managed by an admin on the **Staff add / detail** pages.

- **Enforcement is two-layered:** the staff `/log-shift` location dropdown shows **only the venues the
  staff is assigned to** (fetched from `GET /api/locations/mine`), and the backend independently
  re-checks the assignment when a shift is started or a manual entry is created — a clock-in for an
  unassigned location is rejected with *"You are not assigned to this location."*
- **No assignment ⇒ blocked from clocking in.** A staff with zero assigned locations sees an empty
  dropdown and a warning banner ("ask your manager"); they cannot start a shift until an admin assigns
  at least one location.

Stored as a `StaffLocations` join table (composite PK `EmployeeId` + `LocationId`, additive migration
`AddStaffLocations`). Roles are untouched — pay and display still derive from roles.

---

## Bulk admin actions

Both list pages support acting on many records at once (max 100 per request, server-validated):

- **Staff list** — a *Bulk select* toggle reveals a checkbox column and an action bar:
  **Assign locations** and **Assign roles** (both *additive* — they add to each staff member's
  existing set, never replacing it) and **Deactivate**. Bulk-deactivate is refused if the selection
  would remove the last active admin-portal user.
- **Leave records** — bulk **Approve** / **Reject** (pending only) and **Mark no-show**, alongside the
  per-row actions.

All bulk endpoints follow one backend pattern (load → mutate → single save → per-row audit in the same
transaction); see `BulkUpdateLeaveStatusCommand` and the `Bulk*` staff commands.

---

## Auth & permissions

| Role | Portals | Notes |
|---|---|---|
| `admin` (SuperAdmin) | Both | Full permissions by default; can also use the staff portal as a normal user |
| `manager` | Both | Full permissions by default; can also use the staff portal |
| `employee` | Staff only | Own data only; no admin-portal permissions by default |

Access is controlled by a **permission framework**, not roles directly. Each role carries default
permissions; each user can have per-user **overrides** (grant/revoke). Admin-portal access requires the
`portal.admin` permission; the staff portal is open to any active account. Permissions are resolved
server-side per request (cached, invalidated on change) so a revocation takes effect immediately — they
are **not** baked into the token.

**Sessions** use **httpOnly cookies** (`hrnexus_at` access + `hrnexus_rt` refresh; `SameSite=Strict`,
`Secure` over HTTPS) — tokens are never exposed to JavaScript. The JWT bearer reads the access token
from the cookie.

**Login hardening:** an account locks for 15 min after 5 consecutive failed logins (per-account, returns
429), plus a per-client-IP rate limiter on `/api/auth/*` (default 100/min, tunable via `RateLimit:Auth*`)
as a coarse flood ceiling.

**PII at rest:** IC / passport / phone / date-of-birth are encrypted (AES-256-GCM) in the database via EF
Core value converters; the key comes from `PII_ENCRYPTION_KEY` (`Pii:Key`).

---

## i18n

English and Simplified Chinese. Language files:
```
projects/admin-app/public/i18n/en.json  zh.json
projects/staff-app/public/i18n/en.json  zh.json
```

Always update **both** files when adding UI text.

---

For development patterns, architecture decisions, and coding conventions see **`CLAUDE.md`**.
