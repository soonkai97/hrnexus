# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Commands

### Frontend (Angular workspace root)

```bash
# Install
npm install

# Build shared library first — required before building either app
npx ng build shared-lib

# Serve apps locally (proxy forwards /api/* to :5001)
npx ng serve staff-app   # → http://localhost:4200
npx ng serve admin-app   # → http://localhost:4300

# Production builds
npx ng build staff-app --configuration production
npx ng build admin-app --configuration production

# Tests
npx ng test staff-app
npx ng test admin-app
npx ng test shared-lib
```

### Backend (.NET 10)

```bash
cd backend

# Run API (dev mode, connects to local PostgreSQL on :5432)
dotnet run --project src/Api

# Apply pending EF Core migrations
dotnet ef database update --project src/Infrastructure --startup-project src/Api

# Add a new migration
dotnet ef migrations add <MigrationName> --project src/Infrastructure --startup-project src/Api

# Run all tests
dotnet test

# Run a specific test class
dotnet test --filter "FullyQualifiedName~OvertimeCalculatorTests"
```

### Docker (full stack)

```bash
# Build and start everything (PostgreSQL + .NET API + nginx serving Angular)
docker compose up --build

# Rebuild only the API and web images (after code changes)
docker compose build --no-cache api web
docker compose up -d --no-build

# Restart containers without rebuilding
docker compose up -d --no-build
```

---

## Architecture

### Repository Layout

```
nightclub-workforce/
├── backend/src/
│   ├── Api/            → Controllers, middleware, JSON converters, Program.cs
│   ├── Application/    → MediatR command & query handlers
│   ├── Domain/         → Entities, domain logic, AppConstants
│   └── Infrastructure/ → EF Core, auth services, SignalR hub, Hangfire jobs
├── projects/
│   ├── shared-lib/     → Angular library: services, models, stores, components
│   ├── staff-app/      → Staff-facing PWA
│   └── admin-app/      → Manager/Admin dashboard
├── nginx/              → nginx.conf + Dockerfile (serves both Angular apps)
└── docker-compose.yml
```

### Backend: Clean Architecture + CQRS

Every feature follows the same layering: **Domain → Application → Infrastructure → Api**.

**Domain** has no external dependencies. Entities use factory methods and private setters:
```csharp
var staff = Staff.Create(staffId, firstName, lastName, email, passwordHash, roles);
```

**Application** handlers are MediatR `IRequestHandler<TCommand, TResult>`. Every business operation is a command or query record. Handlers depend on `IAppDbContext`, not `AppDbContext` directly. The interfaces live in `Application/Common/Interfaces/`.

**Infrastructure** registers everything via `AddInfrastructure(config)` called from `Program.cs`. Key registrations: `IAppDbContext` → `AppDbContext`, `ITokenService`, `IPasswordHasher`, `IAuditService`, `IDateTimeProvider`, `IFileStorage`, SignalR hub, Hangfire.

**Api controllers** are thin — validate role/portal access, delegate to `IMediator.Send()`, map exceptions to HTTP status codes.

### Critical Conventions

**DateTime** — PostgreSQL requires `DateTimeKind.Utc`. A custom `UtcDateTimeModelBinderProvider` and `UtcDateTimeJsonConverter` force all incoming `DateTime` values to UTC. When computing local dates in handlers, use `IDateTimeProvider.LocalNow` (configured to `Asia/Kuala_Lumpur`). Never mix `DateTimeKind.Unspecified` with `UtcNow` arithmetic — it produces 8-hour errors.

**Magic strings/numbers** — Use `AppConstants` in `Domain/Constants/AppConstants.cs` for all role names, leave statuses, notification types, tips split methods, and numeric limits (password min length, max page size, tips cap, image size cap, etc.).

**Portal enforcement** — The API checks the `portal` claim in the JWT. Staff-app tokens cannot access admin endpoints, and vice versa. This is enforced at the controller/middleware level, not just at the Angular route level.

**Audit trail** — `IAuditService.LogAsync()` records every significant mutation (entity type, entity ID, action, actor ID/name, detail string). Inject it in any command handler that modifies sensitive data.

**EF Core migrations** — `db.Database.MigrateAsync()` runs automatically at startup (`Program.cs`). Adding a migration is safe in dev; the running container applies it on next restart.

### Frontend: Signals-First Standalone Components

**Build order matters**: `shared-lib` must be built (or rebuilt) before either app when shared library code changes.

**State management** uses Angular signals — `signal()`, `computed()`, `effect()`. There are no NgModules. Every component declares its own `imports: []`. Two store classes (`LocationStore`, `StaffStore`) in shared-lib manage cross-component state via signals.

**The ShellComponent** in each app (not page components) owns the SignalR connection lifecycle — `this.signalR.startConnection()` in the constructor, `this.signalR.stopConnection()` before logout. Never start/stop SignalR from page-level components.

**Feature flags** are fetched at app startup via `FeatureFlagService`. The shell reads them to show/hide nav items. They are PIN-protected at the backend — the `/features` admin route is not listed in the nav and must be accessed by direct URL.

**Shared utilities** in `shared-lib/utils/date.utils.ts`:
- `toLocalDateString(d: Date)` — formats Date as `YYYY-MM-DD` using local wall-clock time (safe for UTC+8, avoids toISOString() previous-day shift)
- `todayLocalString()` — today as `YYYY-MM-DD`
- `MONTH_NAMES` — English month name array aligned with `Date.getMonth()`

All three are exported from `public-api.ts`. Do not write inline date-formatting IIFEs or local month arrays — import from shared-lib.

**`type="email"` and `type="tel"`** must be used on email and phone inputs respectively (not `type="text"`). All data-entry `<input>` elements require `maxlength`. Table header `<th>` elements need `scope="col"`. Icon-only buttons (`✕`, `⋯`) need `aria-label`.

### Key Domain Logic

**Overtime calculation** — `OvertimeCalculator` (static, fully unit-tested) resolves OT rules per role (`*` wildcard fallback), handles midnight-crossing shifts, calculates gross pay = regular pay + OT pay. Rules support per-role threshold hours and multipliers.

**Tips distribution** — Two split methods: `equal` (per-person) or `by_hours` (proportional to elapsed shift time). The distributor reads active WorkLog entries for the shift date, skips clocked-out staff, allocates with penny-rounding to last person. Backend enforces a 50,000 cap; a unique constraint (`LocationId + ShiftDate`) prevents duplicate pools.

**Leave balance** — `LeaveEntitlement` tracks annual/sick days per year. Approval deducts from balance. Overlap detection runs on approval. `AutoRejectOverdueLeaveAsync()` Hangfire job rejects pending requests where StartDate has passed.

**Pay summary** — Gross = regular hours × hourly rate + OT hours × OT rate + tips allocated from `TipsAllocationRecord` for the period. All joins use `DateTime.SpecifyKind(..., DateTimeKind.Utc)` before filtering to avoid EF Core timezone comparison issues.

### Background Jobs (Hangfire)

Recurring jobs registered in `DependencyInjection.cs`:
- Every 15 min: flag WorkLogs open longer than the OT threshold
- Daily 02:00 UTC: auto-reject expired pending leave requests
- Jan 1 01:00 UTC: reset annual leave entitlements

Hangfire dashboard is at `/hangfire` (dev only — blocked in production nginx config).

### Environment & Secrets

Sensitive values come from environment variables (see `docker-compose.yml`). For local dev outside Docker, set them in a `.env` file in the repo root (already gitignored):
```
JWT_SECRET=...
VAPID_PUBLIC_KEY=...
VAPID_PRIVATE_KEY=...
VAPID_SUBJECT=mailto:admin@example.com
```

Generate VAPID keys with:
```bash
npx web-push generate-vapid-keys
```

The `FeatureMgmt__PinHash` env var is a BCrypt hash of the 4-digit PIN used to toggle feature flags.

### Test Projects

- `backend/tests/Application.Tests` — xunit; covers `OvertimeCalculator` (20+ cases), `LeaveRequestDomain`, `LeaveOverlap`
- `backend/tests/Infrastructure.Tests` — xunit; data access and service integration

No Angular unit test files exist yet (`.spec.ts`). Backend tests use in-memory EF Core.
