# HRNexus — Workforce Management System

Internal web application for managing venue staff — attendance, leave, earnings, and payout tracking.

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
│   │       ├── guards/              # authGuard, roleGuard, featureFlagGuard
│   │       └── components/          # Toast, ConfirmDialog, ThemeToggle, etc.
│   ├── admin-app/                   # Manager / admin dashboard (desktop-first)
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

| Route | Feature | Flag |
|---|---|---|
| `/dashboard` | Live KPIs (on-shift count, hours, est. cost) + SignalR activity feed | — |
| `/staff` | Staff list, register, edit, deactivate, reset password | — |
| `/staff/:id` | Staff detail — profile, documents | — |
| `/leave` | Leave requests — approve / reject, create, edit, balance overview | — |
| `/reports` | Workforce attendance report + Staff Ledger payout report | — |
| `/staff-ledger` | Per-staff commission ledger — entries, deductions, loans, settlement | `staffLedger` |
| `/staff-settings` | Work types & settlement statuses (ledger configuration) | — |
| `/tips` | Tips distribution | `tips` |
| `/payroll` | Payroll reporting | `payroll` |
| `/announcements` | Compose and manage staff announcements | `announcements` |
| `/roles` | Role management | — |
| `/locations` | Location management | — |
| `/features` | Feature flag toggle (PIN-protected, admin only) | — |
| `/account` | Own profile, change password | — |

### Staff app (`/`)

| Route | Feature | Flag |
|---|---|---|
| `/home` | Overview dashboard | — |
| `/log-shift` | Clock in / clock out | — |
| `/my-leave` | Submit leave, view history and balance | — |
| `/pay-summary` | View monthly payout summary | `paySummary` |
| `/tips-history` | View tip earnings | `tipsHistory` |
| `/announcements` | Read venue announcements | `announcements` |
| `/profile` | Personal info, change password, language | — |

---

## Pay model

Staff are **commission-based**. Two systems exist with distinct roles:

- **Shift logs** (`/log-shift` → workforce report) — attendance tracking. Records clock-in/out times. The "Est. Labour Cost" in Reports is a rough estimate only.
- **Staff Ledger** — actual pay. Managers manually enter earnings by work type (写钟, 跑台, 包夜, etc.), deductions, loan advances/repayments, and settle entries. The **Export CSV** button on the ledger produces the per-staff monthly payout statement.

---

## Auth & roles

| Role | Portals | Notes |
|---|---|---|
| `admin` | Admin only | Full access including feature flags |
| `manager` | Admin only | All features except feature flag management |
| `employee` | Staff only | Own data only |

JWT (access + refresh token), stored in `localStorage`. Feature flags require an additional PIN unlock stored in `sessionStorage`.

---

## Feature flags

Flags are toggled at runtime via `/features` (admin + PIN required). Default all-off on first deploy.

| Key | Feature |
|---|---|
| `staffLedger` | Staff Ledger page + ledger payout tab in Reports |
| `payroll` | Payroll report |
| `tips` | Tips distribution |
| `tipsHistory` | Tips history (staff app) |
| `paySummary` | Pay summary (staff app) |
| `announcements` | Announcements (both apps) |

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
