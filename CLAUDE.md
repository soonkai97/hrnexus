# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Project overview

Angular 21 + PrimeNG 21 monorepo. One frontend app shares one backend and one library:

| Project | Path | Audience | Notes |
|---|---|---|---|
| `web` | `projects/web` | Everyone (managers and staff) | Responsive PWA (service worker) |
| `shared-lib` | `projects/shared-lib` | The web app | Models, services, stores, guards, pipes, base components |
| `backend` | `backend/` | Backend | ASP.NET Core 10 API (Clean Architecture); `/api/` proxied by nginx |

---

## Commands

```powershell
# ── Deploy (Docker) ──────────────────────────────────────────────────────────
# Always --no-cache for Angular/SCSS/TS changes (avoids stale cached build layer)
docker compose build web --no-cache
docker compose up -d web

# Incremental deploy — only when layer is known-stale (e.g. config-only change)
docker compose build web && docker compose up -d web

# ── Local dev (no Docker) ────────────────────────────────────────────────────
# proxy.conf.json routes /api/ to localhost API
npx ng serve web          # → localhost:4200

# ── Build / test ─────────────────────────────────────────────────────────────
npx ng build web --configuration production
npx ng test shared-lib --no-watch
npx ng test web --no-watch
```

After every deploy, hard-refresh the browser (**Ctrl+Shift+R**) — nginx caches `index.html` without a content hash.

**Deploying:** there is currently no production deployment. On a host that has the Docker stack, `./deploy.sh`
rebuilds and restarts `web` and `./deploy.sh all` also restarts `api`; it enables maintenance mode first and
removes it after the containers are healthy (`--wait`).

> **Rule 18**: After any coding session, always commit to GitHub AND rebuild Docker before closing.  
> See **Rule 18 — Docker and GitHub must always reflect the latest code** in the Process rules section.

---

## Project structure

```
projects/
├── web/src/app/
│   ├── app.ts                    # root component (<router-outlet />)
│   ├── app.config.ts             # providers (HttpClient, PrimeNG, guards)
│   ├── app.routes.ts             # all routes
│   ├── shell/                    # layout shell (sidebar, topbar, nav filtered by permission)
│   └── features/                 # one folder per route page
│       ├── auth/                 # login, forgot-password
│       ├── home/                 # self-service: any signed-in account
│       ├── my-leave/
│       ├── profile/
│       ├── dashboard/            # management pages: permission-gated
│       ├── staff/                # staff list + staff-detail
│       ├── leave/
│       ├── roles/
│       ├── policy/               # company policy (working week, leave rules)
│       ├── payroll-locks/
│       └── cash-advances/
│
└── shared-lib/src/lib/
    ├── models/                   # DTOs and interfaces
    ├── services/                 # HTTP services + ToastService + ConfirmService
    ├── stores/                   # signal-based state (StaffStore)
    ├── guards/                   # authGuard, permissionGuard, unsavedChangesGuard
    ├── auth/                     # AuthService, jwt/error/maintenance interceptors, ForgotPasswordBase
    ├── pipes/                    # HmPipe (24-hour time)
    ├── directives/               # TrimInputDirective
    ├── utils/                    # date and password helpers
    └── components/               # ToastComponent, ConfirmDialogComponent, NotificationBellComponent
```

Feature files follow the pattern: `{name}.component.ts`, `.html`, `.scss` (and `.routes.ts` for sub-routes).

---

## Angular setup

- **Fully standalone components — no NgModules anywhere.**
- Bootstrap: `main.ts` → `bootstrapApplication(App, appConfig)`
- Root `App` component calls `appUpdate.init()` in `ngOnInit` (ensures SW update check fires on every page load, regardless of auth state)
- All feature routes are lazy-loaded via `loadComponent()` / `loadChildren()`

**`app.config.ts` key providers:**
```typescript
provideRouter(routes, withComponentInputBinding())
provideHttpClient(withInterceptors([maintenanceInterceptor, errorInterceptor, jwtInterceptor]))
provideAppInitializer(async () => {
  // Hits /api/health before anything renders — bypasses SW cache (not in dataGroups).
  // On 503: maintenanceInterceptor redirects to /maintenance.html, bootstrap is blocked.
  // On network error: swallowed, app boots from cache (offline).
  const http = inject(HttpClient);
  try { await firstValueFrom(http.get('/api/health', { responseType: 'text' })); }
  catch (err: unknown) {
    if ((err as { status?: number })?.status === 503)
      await new Promise<never>(() => {});
  }
})
providePrimeNG({ theme: { preset: Aura, options: { darkModeSelector: false } } })   // one light theme; no dark mode
MessageService, ConfirmationService
provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: 'registerWhenStable:30000' })
```

---

## Routing

All routes in `app.routes.ts`. Pattern: lazy load + guards.

One app, one sign-in URL for everyone. Everything below `/auth` sits in the shell behind `authGuard`;
management pages add a `permissionGuard`. The landing redirect is `/home` for everyone and the
`permissionGuard` fallback is `/home`. The shell menu is filtered by permission, with a "My work" group
(Home, My Leave, Profile) always shown.
```
/home, /my-leave, /profile   authGuard only (any signed-in active account)
/dashboard             permissionGuard('reports.view')
/staff, /staff/:id     permissionGuard('staff.view')
/leave                 permissionGuard('leave.approve')
/roles                 permissionGuard('roles.manage')
/policy                permissionGuard('policy.manage')
/payroll-locks         permissionGuard('payroll.lock')
/cash-advances         permissionGuard('cashadvance.manage')
/auth/**               login, forgot-password (public)
```

`/account` was removed; own profile and push notifications live on `/profile`. There is no `portal` login
field and no `portal.admin` permission.

**Guards:**
- `authGuard` — passes when `auth.isAuthenticated()` (a user session is known locally). The access token is an httpOnly cookie JS cannot read, so the server is the real enforcer: an expired cookie 401s and `jwtInterceptor` refreshes or logs out.
- `permissionGuard(key, fallback)` — checks the current user holds `key`; redirects to `fallback` otherwise
- `unsavedChangesGuard` — `canDeactivate` prompt for forms; skipped while `auth.loggingOut()`

---

## API / HTTP layer

**Base URL:** `/api/` (relative — nginx proxies it to the .NET container at runtime; `proxy.conf.json` does the same for local dev).

No global HTTP service. Components/stores call `HttpClient` directly:
```typescript
this.http.get<T[]>('/api/staff')
this.http.post<T>('/api/staff', body)
this.http.put<T>(`/api/staff/${id}`, body)
this.http.delete<void>(`/api/staff/${id}`)
```

**Error handling:** 401s are intercepted (see jwtInterceptor). All other errors propagate to the subscriber's `error` callback. Components call `this.toast.error(err?.error?.error ?? 'Generic error message.')`; shared-lib also exports `apiErrorMessage(err)` for the same extraction.

**No `environment.ts` files.** All config is either hardcoded relative URLs or fetched from the API at runtime.

---

## Authentication

**AuthService** (`shared-lib/src/lib/auth/auth.service.ts`):

```typescript
// Signals
user = signal<AuthUser | null>(...)      // non-sensitive profile, restored from localStorage['auth_user']
isAuthenticated = computed(...)          // user() !== null
loggingOut = signal(false)               // true while an explicit sign-out navigates away

// Methods
login(req: LoginRequest): Observable<AuthResponse>
logout(): void                           // POST /api/auth/logout, clears the profile, navigates to /auth/login
refreshToken(): Observable<AuthResponse>
forgotPassword(staffId: string): Observable<ForgotPasswordResponse>
updateProfile(phoneNumber: string | null): Observable<void>
```

**Session storage:** tokens live in **httpOnly cookies** (`hrnexus_at`, `hrnexus_rt`) set by the server; JavaScript never sees them. Only the non-sensitive `AuthUser` JSON is kept in `localStorage['auth_user']`. Legacy `access_token` / `refresh_token` keys are purged on service init.

**Interceptors** (registered in `app.config.ts` as `[maintenanceInterceptor, errorInterceptor, jwtInterceptor]`):
- `jwtInterceptor` — clones every request with `withCredentials: true` (no Bearer header). On a 401 from a non-`/api/auth/` URL it refreshes once (concurrent requests wait on the same refresh) and replays; on refresh failure `refreshToken()` has already logged out.
- `errorInterceptor` — logs failures other than 401/503 and rethrows unchanged.
- `maintenanceInterceptor` — redirects to `/maintenance.html` on HTTP 503.

---

## Shared-lib exports

**Models** (key ones): `Staff`, `StaffRole`, `RegisterStaffRequest`, `UpdateStaffRequest`, `AuthUser`, `AuthResponse`, `LoginRequest`, `LeaveRequest`, `LeaveBalance`, `NotificationDto`, `PagedResult<T>`

**Services:**
| Service | Purpose |
|---|---|
| `AuthService` | Login, logout, cookie-session refresh, forgot-password, profile update |
| `PermissionService` | Current user's effective permissions (`load()`, `has(key)`) |
| `PermissionAdminService` | Permission catalogue, role permissions, per-user overrides |
| `StaffManagementService` | Employee CRUD (get, register, update, toggleActive, getRoles) + bulk actions |
| `AdminService` | Dashboard stats (`getDashboard()`) |
| `LeaveService` | Leave requests, balances, bulk actions |
| `CashAdvanceService` | Cash advance records (list, create, settle, delete) |
| `PayrollLockService` | Payroll month locks (list, lock, reopen) |
| `NotificationService` | Notification list + unread count |
| `PushService` | Web Push public key + subscribe/unsubscribe |
| `SignalRService` | WebSocket connection to `/hubs/shift` (the notifications hub; rename planned) |
| `ToastService` | `success/error/warning/info(msg)` |
| `ConfirmService` | `confirm(opts) → Observable<boolean>` |
| `AppUpdateService` | Service-worker update check (`init()` is called from the root `App`) |
| `VersionService` | Build version info |

**Guards:** `authGuard`, `permissionGuard`, `unsavedChangesGuard`

**Components:**
- `ToastComponent` (`<nc-toast />`) — mount once in the app shell
- `ConfirmDialogComponent` (`<nc-confirm-dialog />`) — mount once in the app shell
- `NotificationBellComponent` — topbar bell with popover; takes no `portal` input and shows every notification the server sent

**Base classes:**
- `ForgotPasswordBase` — logic for forgot-password pages (the web app's forgot-password page extends this)

**Pipes:** `HmPipe` (`| hm`) — 24-hour time display: `"8:30"` → `"08:30"`, `null` → `"—"` (was `AmPmPipe`; the app shows 24-hour time)
**Directives:** `TrimInputDirective`

**Utilities:** `provideNoAutofill()`, date utils, password utils

---

## Signal-based stores

Stores are `@Injectable({ providedIn: 'root' })` singletons. They hold all list/async state for their domain.

**Pattern:**
```typescript
@Injectable({ providedIn: 'root' })
export class StaffStore {
  // Private writable signals
  readonly #staff      = signal<Staff[]>([]);
  readonly #roles      = signal<StaffRole[]>([]);
  readonly #loading    = signal(false);
  readonly #totalCount = signal(0);

  // Public read-only
  readonly staff      = this.#staff.asReadonly();
  readonly roles      = this.#roles.asReadonly();
  readonly loading    = this.#loading.asReadonly();
  readonly totalCount = this.#totalCount.asReadonly();

  // Methods update signals; mutations return Observable (caller subscribes)
  loadStaff(search?: string, page = 1, pageSize = 10): void { ... }
  loadRoles(): void { ... }
  register(req: RegisterStaffRequest): Observable<Staff> { ... }
  update(id: string, req: UpdateStaffRequest): Observable<Staff> { ... }
  deleteStaff(id: string): Observable<void> { ... }
}
```

Available stores: `StaffStore` (confirmed). Other domains use service classes directly.

**Usage in components:**
```typescript
private readonly staffStore = inject(StaffStore);

ngOnInit() { this.staffStore.loadStaff(); }

staff    = this.staffStore.staff;       // signal — use in template as staff()
loading  = this.staffStore.loading;
```

---

## State / signals pattern

All component-level state uses Angular signals. No `BehaviorSubject` or `Subject` in new code.

```typescript
// State
items   = signal<Item[]>([]);
loading = signal(false);
error   = signal('');

// Derived
total = computed(() => this.items().length);
isEmpty = computed(() => !this.loading() && this.items().length === 0);

// Update
this.loading.set(true);
this.items.update(list => [...list, newItem]);
```

---

## Toast and confirm patterns

**Toast:**
```typescript
private readonly toast = inject(ToastService);

this.toast.success('Saved.');
this.toast.error('Failed to save.');
this.toast.warning('Period not closed yet.');
this.toast.info('Loading…');
```

**Confirm dialog:**
```typescript
private readonly confirmSvc = inject(ConfirmService);

this.confirmSvc.confirm({
  message: 'Delete this staff member?',
  danger: true,            // red confirm button
}).subscribe(confirmed => {
  if (!confirmed) return;
  this.staffStore.deleteStaff(id).subscribe({
    next: () => this.toast.success('Deleted.'),
    error: (err) => this.toast.error(err?.error?.error ?? 'Failed.'),
  });
});
```

Both components (`<nc-toast />` and `<nc-confirm-dialog />`) must be in the app's shell template. They are already there — do not add them again.

---

## Form pattern

**Template-driven (`ngModel`) is the default — most forms are a plain object + template bindings.** The exceptions are the auth forms: the web app's `LoginComponent` uses **reactive forms** (`FormBuilder`/`FormGroup`/`Validators`) because they need synchronous, field-level validation state. New feature forms should follow the `ngModel` pattern below unless they have the same need.

```typescript
// Form state as a plain object
form = {
  staffId: '', firstName: '', lastName: '',
  email: '', roles: [] as string[],
  phoneNumber: '', dateOfBirth: '',
};
saving = signal(false);
error  = signal('');
showForm = signal(false);

// Validation — getter or inline in template
get isFormValid(): boolean {
  return !!this.form.staffId.trim() && !!this.form.email.trim() && ...;
}

// Submit
save(): void {
  if (!this.isFormValid) return;
  this.saving.set(true);
  this.staffStore.register(this.form).subscribe({
    next: () => { this.toast.success('Saved.'); this.closeForm(); },
    error: (err) => this.error.set(err?.error?.error ?? 'Failed.'),
    complete: () => this.saving.set(false),
  });
}

// Toggle / reset
openForm(): void { this.showForm.set(true); this.error.set(''); }
closeForm(): void { this.showForm.set(false); this.form = { staffId: '', ... }; }
```

**Template (inline form pattern):**
```html
@if (showForm()) {
  <p-card styleClass="form-card">
    <div class="form-grid">
      <div class="field">
        <label><span class="req">*</span> Staff ID</label>
        <input pInputText [(ngModel)]="form.staffId" />
      </div>
      <!-- more fields -->
    </div>
    <div class="form-actions">
      <p-button label="Cancel" [text]="true" (onClick)="closeForm()" />
      <p-button label="Save" severity="primary"
                [disabled]="!isFormValid || saving()" [loading]="saving()"
                (onClick)="save()" />
    </div>
  </p-card>
}
```

---

## Loading / skeleton pattern

```typescript
loading = signal(false);

load(): void {
  this.loading.set(true);
  this.http.get<T[]>('/api/items').subscribe({
    next: (data) => this.items.set(data),
    error: (err) => this.toast.error('...'),
    complete: () => this.loading.set(false),
  });
}
```

```html
@if (loading()) {
  <!-- skeleton rows -->
  @for (i of [1,2,3,4]; track i) {
    <div class="skeleton-row">
      <p-skeleton width="30%" height="0.7rem" />
      <p-skeleton width="50%" height="0.7rem" />
    </div>
  }
} @else if (items().length === 0) {
  <div class="empty-state">No data.</div>
} @else {
  <!-- actual content -->
}
```

Rule: use `<p-skeleton>` for initial page loads (>150 ms). Use `[loading]="saving()"` on PrimeNG buttons for action waits. Section-level reloads (e.g. paginating) reuse the same skeleton/`loading()` guard — there is no full-page `<p-progressspinner>` anywhere in the codebase, and none should be added (skeletons + button spinners are the only loading patterns). `<p-progressbar>` is reserved for data visualization (leave-balance ratios, cost bars), never as a loading indicator.

---

## Language

The product is **English only**. UI text is written as inline English literals in templates and
components. Never add `ngx-translate`, translation pipes/services, locale JSON files or i18n keys.
Reintroducing a second language needs a new phase document and an ADR (see
`docs/architecture/adr-english-only.md`).

---

## SCSS and styling

Every component SCSS starts with:
```scss
@use 'styles/theme' as *;
```

**Key design tokens:**

| Category | Variables |
|---|---|
| Surfaces | `$bg-void`, `$bg-base`, `$bg-surface`, `$bg-raised` |
| Neon accents | `$neon-purple` (#a855f7), `$neon-pink`, `$neon-green`, `$neon-amber`, `$neon-blue` |
| Text | `$text-primary`, `$text-secondary`, `$text-muted` |
| Borders | `$border-subtle`, `$border-glow` |
| Radius | `$radius-sm` (8px), `$radius-md` (12px), `$radius-lg` (18px), `$radius-pill` |
| Transitions | `$transition-fast` (0.15s), `$transition-base` (0.25s) |
| Glows | `$glow-purple`, `$glow-green`, `$glow-sm` |

Tokens resolve to CSS custom properties (`var(--nc-bg-surface)` etc.). The app has **one light theme** (no dark mode, no toggle; Phase 7p): the palette is defined once on `:root` in `styles.scss`, and the element colour rules under the second `:root { ... }` block keep their specificity by staying scoped to `:root`. Do not add `[data-theme]`, `prefers-color-scheme` or a theme service.

### Critical SCSS gotcha — `:host :deep()` vs `.[class] :deep()`

**Never use `.[class] :deep(...)` to target PrimeNG internals.** Angular compiles `.table-card :deep(.foo)` to `.table-card[_ngcontent-C] .foo`. The class applied via `styleClass` on `<p-card>` lands on p-card's internal template div, which has p-card's own `_ngcontent` — not the parent's. The rule silently never fires.

```scss
// ✗ broken
.table-card :deep(.p-datatable-table) { display: block; }

// ✓ correct — [_nghost-C] is always on the component's host element
:host :deep(.p-datatable-table) { display: block; }
```

For PrimeNG overrides that must work globally, add them to `styles.scss` (no scoping).

### Responsive p-table stack layout

Tables collapse to card-stack at ≤640 px. Pattern (canonical: `staff.component.scss`):

1. Add `<span class="p-column-title">Label</span>` inside every `<td>` in the template.
2. Global `styles.scss` already hides `.p-column-title { display: none; }`.
3. In the component SCSS:

```scss
@media (max-width: 640px) {
  :host :deep(.p-datatable-table)  { display: block; }
  :host :deep(.p-datatable-thead)  { display: none; }

  :host :deep(.p-datatable-tbody > tr) {
    display: block;
    border: 1px solid $border-subtle;
    border-radius: $radius-md;
    margin: 0.5rem 0.75rem;
    padding: 0.25rem 0;
    background: $bg-surface;
  }

  :host :deep(.p-datatable-tbody > tr > td) {
    display: flex;
    align-items: center;
    gap: 0.65rem;
    padding: 0.45rem 0.85rem;
    border: none;
    border-bottom: 1px solid $border-subtle;
    font-size: 0.85rem;
    &:last-child { border-bottom: none; }

    .p-column-title {
      display: block;          // re-enable here
      font-size: 0.68rem;
      font-weight: 700;
      color: $text-muted;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      min-width: 70px;
      flex-shrink: 0;
    }
  }
}
```

### PrimeNG overlays

Any `<p-menu>`, `<p-select>`, `<p-multiselect>`, or `<p-datepicker>` inside a card with `backdrop-filter` must use `appendTo="body"`. Without it the overlay panel is clipped inside the card's CSS stacking context.

---

## Deployment (Docker)

**docker-compose.yml services:** `web` (nginx), `api` (.NET 10), `postgres` (PostgreSQL 17).

**Build stages (Dockerfile for `web`):**
1. Node 22 builder: `npm ci` → build `shared-lib` → build `web`
2. nginx alpine: copies the single `web` dist to `/usr/share/nginx/web`, one server on container port 80 (compose publishes `8080:80` only)

**nginx behaviour:**
- `try_files $uri $uri/ /index.html` — SPA client-side routing
- `/api/` → proxied to `http://api:8080`
- `/hubs/` → proxied with WebSocket upgrade headers (SignalR)
- `index.html` — `no-cache` headers so new deploys take effect immediately
- Static file extensions (`.js`, `.css`, `.json`, `.webmanifest`, fonts, images) — **always served even during maintenance** so the SW can download updated bundles

**No `environment.ts` files.** All config is relative URLs or runtime API calls.

### Deployment status

There is currently **no production deployment** (the server was removed on 13 Nov 2026). When one exists:
run the base `docker-compose.yml` with the variables in `.env.example` (never commit `.env`), use
`./deploy.sh`, and recreate a deploy workflow as described in `docs/process/git-workflow.md`.
Production keys (`JWT_SECRET`, `PII_ENCRYPTION_KEY`, database password, VAPID keys) come from the server
environment only. **Never change `PII_ENCRYPTION_KEY` once data is encrypted.**

---

## Maintenance mode

During `./deploy.sh`, users see a styled maintenance page instead of a blank error.

### How it works end-to-end

1. `deploy.sh` calls `nginx -s reload` (picks up any nginx config changes), then `touch maintenance/on`
2. nginx checks `if (-f /tmp/maintenance/on)` on every request — `/api/`, `/hubs/`, `index.html`, `location /` — and returns 503 with `maintenance.html`
3. `maintenanceInterceptor` (shared-lib) catches any HTTP 503 from Angular `HttpClient` and does `window.location.href = '/maintenance.html'`
4. `provideAppInitializer` calls `GET /api/health` before Angular renders anything — guarantees maintenance is detected even on phones with cached apps
5. `deploy.sh` uses `--wait` so the flag is only removed after all container health checks pass
6. `maintenance.html` polls `/api/health` every 30 s; when it returns 200 it navigates to `/` (not `reload()` — that would loop back to the same HTML page)

### Service worker + phone behaviour

The Angular SW caches the app. During maintenance, the cached old app would normally bypass the interceptor. Three mechanisms work together to fix this:

- **Static assets pass through nginx during maintenance** — `.js`, `.css`, `.json`, `ngsw.json`, `ngsw-worker.js` are served without the maintenance check. This lets the SW download the new app version in the background.
- **`AppUpdateService.init()` is called in the root `App` component** (not just in `ShellComponent`) — so it fires on every page load regardless of auth state, triggering `activateUpdate()` + reload as soon as the new version is ready.
- **`provideAppInitializer`** hits `/api/health` (not in any SW `dataGroups`) before rendering — catches maintenance even from a freshly activated new app.

### SignalR during maintenance

SignalR uses its own fetch for `/hubs/negotiate` — it bypasses `HttpClient` and `maintenanceInterceptor`. `SignalRService` uses a `FilteredLogger` that silently drops messages containing `<!DOCTYPE` or `Status code '503'`. The `connect()` catch handler also redirects to `/maintenance.html` on 503.

### Key files

| File | Role |
|---|---|
| `nginx/maintenance.html` | Maintenance page — dark glassmorphism, animated gears, elapsed timer, 30s health poll |
| `nginx/nginx.conf` | `if (-f /tmp/maintenance/on)` checks in every location block |
| `maintenance/` | Host directory bind-mounted to `/tmp/maintenance` in the nginx container; `on` file is git-ignored |
| `deploy.sh` | Creates/removes `maintenance/on`; uses `--wait` |
| `shared-lib/auth/maintenance.interceptor.ts` | `HttpInterceptorFn` — catches 503 and redirects |
| `shared-lib/services/signalr.service.ts` | `FilteredLogger` + 503 detection in `connect()` catch |

---

## Backup & recovery

Take a database backup before any deployment that holds data or runs a data-dropping migration.

- Script: `scripts/backup-db.sh` — `pg_dump` → gzipped file in `/opt/hrnexus/backups/`, keeps the last **7 days**; schedule it with cron on the host.
- Restore: `scripts/restore-db.sh <file>.sql.gz` (prompts "YES" confirmation, destructive).
- Whole-server snapshots depend on the hosting provider and are set up when a server exists.

---

## Observability

Two phases. **Phase 1** (always on): Serilog structured logging + a `CorrelationId`/`StaffId`
enricher on every event (`HttpContextEnricher`), written to console (human-readable) and a rolling
JSON file (`logs/hrnexus-.log`, 7-day retention, in the `logs` volume). **Phase 2** (opt-in,
**dormant by default** — local/CI/prod are unchanged until explicitly enabled):

| Piece | What | Toggle |
|---|---|---|
| **Seq log sink** | Ships structured logs to Seq for search/dashboards/alerts | env `Seq__ServerUrl` (+ optional `Seq__ApiKey`) |
| **Request logging** | One completion log per request (method/path/status/elapsed), with the enrichers | always on (`UseSerilogRequestLogging`) |
| **OpenTelemetry** | Traces + metrics (ASP.NET + HttpClient + runtime) via OTLP | env `OTEL_EXPORTER_OTLP_ENDPOINT` |
| **Seq container** | `datalust/seq`, **loopback-bound** (`127.0.0.1:5341`), no public exposure | compose profile `observability` |

When the env vars are unset the Seq sink and OpenTelemetry are **not wired at all** (zero overhead);
the rolling file + console logging still work.

### Health checks
- `GET /api/health` — simple liveness JSON; **polled by maintenance mode + the container healthcheck — do not change.**
- `GET /health/live` — ASP.NET liveness (process up, no dependency probes).
- `GET /health/ready` — readiness; includes a DB-reachability probe (`DbReadyHealthCheck`).
- `/health/*` are **not** proxied by nginx (internal probes). Hit them on the api container directly.

### Security headers
Set at the **nginx edge** for all responses (incl. the SPA HTML): `X-Content-Type-Options`,
`X-Frame-Options`, `X-XSS-Protection`, `Referrer-Policy`, `Permissions-Policy` (see `nginx/nginx.conf`).
Do **not** duplicate these at the API — doing so produces invalid doubled header values. The CSP is
**enforced** (not report-only); `inlineCritical:false` keeps `script-src 'self'` strict.

### Auth & data-protection hardening
- **Tokens in httpOnly cookies** (not localStorage): login/refresh set `hrnexus_at` + `hrnexus_rt`
  (`HttpOnly; SameSite=Strict; Secure when HTTPS`); the bearer pipeline reads the access cookie.
  `POST /api/auth/logout` expires them + clears the DB refresh token. nginx forwards
  `X-Forwarded-Proto` so the Secure flag is correct behind the proxy.
- **Prompt revocation**: the access JWT is **short-lived** (`Jwt:AccessTokenMinutes`, default **30 min**)
  and `RefreshTokenCommand` re-checks `IsActive` + re-issues fresh role/permission claims on every
  rotation, so a deactivation or role change takes effect within the access-token window rather than
  lingering for the cookie's 30-day lifetime. `Staff.Deactivate()` also **clears the refresh token**,
  ending the session at the next refresh.
- **PII encrypted at rest**: `IFieldEncryptor` (AES-256-GCM) encrypts `Staff` IcNo / PassportNo /
  PhoneNumber / DateOfBirth via EF value converters; ciphertext carries an `enc1:` marker and reads
  fall back to legacy plaintext, so a guarded startup `PiiBackfill` lazily encrypts old rows. Email
  stays plaintext (it has a unique index + lookups). Key = `PII_ENCRYPTION_KEY` (≥32 chars).
  **⚠️ Never change `PII_ENCRYPTION_KEY` once data is encrypted — the data becomes unreadable.**
  The `EncryptStaffPii` migration changes DOB `timestamptz → text`; deploy it in a maintenance
  window with a fresh DB backup.

### Enabling Seq (opt-in)
Seq has **no authentication** in the free tier, so it is loopback-bound — reach the UI **only through an SSH
tunnel**, never a public port.

```bash
# Add to .env:  SEQ_SERVER_URL=http://seq   (optional traces: OTEL_EXPORTER_OTLP_ENDPOINT=http://seq/ingest/otlp/v1/traces)
docker compose --profile observability up -d

# From your laptop — tunnel, then browse http://localhost:5341:
ssh -L 5341:localhost:5341 <user>@<host>
```
Disable: remove `SEQ_SERVER_URL` from `.env`, `docker compose up -d` (recreates api without the sink), then
`docker compose --profile observability stop seq`. Seq uses ~150–300 MB RAM and stores logs + traces, not
metrics: point `OTEL_EXPORTER_OTLP_ENDPOINT` at a real OTLP collector if you later want metrics dashboards.

---

## Staff pay model — design intent

**Clock-in/out (shift logs) was removed** (Phase 7, `docs/process/phase-07-remove-shift-log.md`):
the `WorkLog` feature, its hourly-pay calculation (hours × `HourlyRateSnapshot`), the "Est. Labour
Cost" report/dashboard figures, payroll export and payslips that derived from it no longer exist.
Hourly pay is gone from the product: `Role.HourlyRate`, `Role.PayType` and `RoleRate` were removed in
Phase 8a. Pay is a **monthly salary** (`docs/process/phase-08-payroll.md`). Don't reintroduce attendance
or hourly pay without a new phase document.

### Salary and employment (Phase 8a)

- **`SalaryProfile`**: one row per effective date, **append-only** (a raise adds a row; rows are never edited
  or deleted; the same `EffectiveFrom` twice is a 409). The profile in effect is the latest row whose date has
  arrived. Salary, bank account and holder are **encrypted at rest** (same `IFieldEncryptor` as Staff PII); the
  money column is text ciphertext, so never query or sort on it. Money is `decimal` (MYR, 2 places, at most
  RM 1,000,000); the frontend only displays and sends it, it never does arithmetic on money.
- **Masking**: salary is returned **only** by `GET /api/staff/{id}/salary` (`payroll.view`); `POST` and
  `PUT /api/staff/{id}/employment` need `payroll.manage`. The ordinary staff endpoints never carry salary.
- **Audit rows say THAT pay changed and from when, never the amounts or the account number.**
- Employment details on `Staff` (`CitizenshipStatus`: `citizen` | `permanent-resident`, start and end date)
  are what payroll proration and statutory class need. Pay history is a payroll record kept for the retention
  period, so it is not removed when a person is deleted.

> The former in-app **Staff Ledger** and **Staff Settings** features (per-staff earnings ledger,
> work types, settlements) were **removed** — the venue handles final payslips and statutory
> deductions (EPF / SOCSO / tax) outside the system.
> The `StaffEntry`/`StaffExpense`/`StaffLoan`/`StaffWorkType`/`StaffSettlement`/… tables were dropped
> (migration `DropStaffLedgerTables`). Don't reintroduce the `ledger.manage`/`config.manage` permissions.

---

## Company policy (Phase 7m)

HR rules that differ between companies are **settings, not constants**. One installation serves one company;
`CompanyPolicy` is a single row (`Id = 1`; when nothing is saved the defaults apply, and the defaults equal the
rules the product shipped with: Mon-Fri, 8h, 3 days' notice, 30-day cap, capped carry-over of 14h, divisor 26).

- **Read** with `CompanyPolicyReader.GetAsync(db, ct)` (never read `AppConstants` for policy). It holds the working
  week (`WorkSchedule`: ISO weekdays and hours per day), annual-leave notice days, the longest single leave request,
  the carry-over rule (`none` lapses, `capped` up to the cap, `unlimited` = cumulative) and the payroll unpaid-leave
  divisor. `LeaveHoursCalculator` is pure and takes the `WorkSchedule` as an argument.
- **API:** `GET /api/company-policy` for any signed-in user (every client previews hours with the working week);
  `PUT` needs `policy.manage`, validates every field, and refuses a stale `version` (409).
- **Frontend:** `CompanyPolicyService` (shared-lib) holds the policy (defaults until loaded; the shell loads it once per
  session) and exposes `schedule`. `leaveHours()` / `leaveHoursInYear()` take that `WorkSchedule` (`{ days: ISO[], hours }`,
  default Mon-Fri 8h), and the two leave pages and their calendars follow it. The **Company Policy** page (`/policy`,
  `policy.manage`, Setup group) edits it with a live example; a 409 asks the administrator to reload instead of overwriting.
  The E2E policy spec changes the global policy, so it restores what it found.
- **Policy changes are not retroactive:** stored leave `Hours` stay authoritative. Only the year split of an old
  multi-day leave that crosses New Year reads the current schedule.
- **Carry-over** (`CarryOverLeaveEntitlementCommand(FromYear, ToYear)`) follows the policy; it takes no cap argument.
- Tests that book a single day must use a day that is a working day under the schedule they set up.

## Leave rules (Phase 7l)

Rules the leave handlers enforce; each has a test in `LeaveFixesTests` (`docs/process/phase-07l-leave-fixes.md`).

- **Status state machine** (`LeaveRequest.EnsureCanBecome`): `pending → approved | rejected | cancelled`,
  `approved → rejected | cancelled`; `rejected` and `cancelled` are **final** (the person applies again).
  Staying in the same status is allowed so notes/dates can be edited. `Cancel()` on a closed leave throws.
- **Overlap** (`UpsertLeaveCommand.EnsureNoOverlapAsync`): only `pending` and `approved` leave blocks a new
  request; `rejected` and `cancelled` never do.
- **No self-approval**: the admin endpoints pass the signed-in user as `ActingUserId`; approving or rejecting
  your own leave (single, or any own pending leave in a bulk selection) throws `InvalidOperationException` (409).
  Recording your own leave as `pending` is allowed, so an installation needs a second person with
  `leave.approve`. The check uses the **stored** owner of an existing leave, never the client-supplied `employeeId`.
  New code paths that decide leave must pass `ActingUserId`; `null` means self-service or a background job.
- **Hours are server-owned working days** (`LeaveHoursCalculator`): a working day is one of the company's working
  days (default Monday to Friday) and as long as the policy's hours per day (default 8h). Several days = working days x 8 (times ignored); one weekday = the
  clock difference of the two times capped at the working-day length, or a full day with no times; a range with no
  working day is a 400.
  The `hours` in a request body is ignored. The frontend mirrors the rule in `leaveHours()` (shared-lib); both
  are tested against `tests/fixtures/leave-hours.json`, so change the rule and the fixture together. Leave
  longer than the policy's limit (default 30 days) is refused. Public holidays are not modelled yet (Phase 8).
- **Charged per year** (`LeaveHoursCalculator.HoursInYear`): a leave across 31 Dec / 1 Jan is charged to each
  year by the working days that fall in it, in the apply-time balance check (every touched year needs an
  entitlement), balances, the summary and carry-over. Tests that book a single day must use a weekday.
- **Edits** (`UpsertLeaveCommand`): an update must name the leave's real owner (a different `employeeId` is a
  400), so the balance and overlap checks always run on the right person. `rejected` and `cancelled` leave is
  immutable. Leave that has **ended** keeps its dates, times and type; only its status can change, or it can
  become a `no-show`. A status-only edit keeps the stored hours, so a row recorded before the working-day rule
  (for example a weekend) can still be approved, rejected or marked no-show.
- **No-show** (`BulkMarkLeaveNoShowCommand`) frees the annual hours a leave used, so it follows the self-approval
  rule too (own leave in the selection throws) and skips rejected/cancelled leave.
- **Withdrawal**: staff cancelling an **approved** leave notifies every other active holder of `leave.approve`
  ("Leave Withdrawn", `leave_pending` type so the bell links to `/leave`); cancelling a pending one is silent.

---

## Bulk admin actions

Bulk operations act on a selected set of records in one request. **Canonical backend pattern**
(`BulkUpdateLeaveStatusCommand`): validate a **≤100** batch, load all rows, mutate in a loop, **one**
`SaveChangesAsync`, and `audit.LogAsync(...)` per row *inside the loop* (so audit rows commit in the
same transaction); catch `DbUpdateConcurrencyException` → clean "refresh and try again" `InvalidOperationException`.

> **Audit performer**: pass `performedById: null` to `audit.LogAsync(...)` and `AuditService` fills in
> the **authenticated caller** (`ICurrentUserService.UserId`/`UserName`) — so admin/bulk commands record
> *which* admin acted instead of a `"Admin"`/null placeholder. Pass an explicit performer only when the
> actor differs from the caller (e.g. a background job, or attributing to a specific staff member).

Current bulk commands:
- **Staff** (`Application/Staff/Commands/BulkStaffCommands.cs`, `StaffController` `POST bulk/roles`,
  `bulk/deactivate`, gated `staff.manage`): `BulkAssignRolesCommand` is **additive** — it calls the
  **`Staff.AddRoles`** union helper (NOT `SetRoles`, which replaces). `BulkDeactivateStaffCommand` applies a
  **whole-selection** last-administrator guard (`RolesManageInvariant` on `roles.manage`: refuses if the batch would leave no active holder) —
  the per-row `DeactivateStaffCommand` guard can't catch that because un-saved in-memory changes still
  read as committed-active. Roles are validated against the `Roles` table (the admin dropdown source).
- **Leave** (`Application/Admin/Commands/BulkMarkLeaveNoShowCommand.cs`, `AdminController`
  `POST leave/bulk-no-show`, gated `leave.manage`): flips each record's type to `no-show`, preserving
  status; skips already-no-show rows.

**Frontend:** the leave page owns the original selection UI (`bulkSelecting`/`selectedIds` signals +
checkbox column + action bar); the staff list mirrors it. Service methods live on
`StaffManagementService` / `LeaveService` (+ `StaffStore` delegators). Bulk commands return
`{ count }`. Backend `ArgumentException` → 400 surfaced as a toast.

---

## Process rules

These rules apply to every task unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.

### Rule 1 — Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.

### Rule 2 — Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

### Rule 3 — Surgical Changes
Touch only what you must. Clean up only your own mess.
Don't "improve" adjacent code, comments, or formatting.
Don't refactor what isn't broken. Match existing style.

### Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

### Rule 5 — Use the model only for judgment calls
Use me for: classification, drafting, summarization, extraction.
Do NOT use me for: routing, retries, deterministic transforms.
If code can answer, code answers.

### Rule 6 — Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens.
If approaching budget, summarize and start fresh.
Surface the breach. Do not silently overrun.

### Rule 7 — Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

### Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

### Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

### Rule 10 — Checkpoint after every significant step
Summarize what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

### Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside the codebase.
If you genuinely think a convention is harmful, surface it. Don't fork silently.

### Rule 12 — Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.

### Rule 18 — Docker and GitHub must always reflect the latest code

Git workflow, hooks, required CI checks and the manual deploy workflow: [`docs/process/git-workflow.md`](docs/process/git-workflow.md).

After any session that modifies source files, spec files, config, or migrations:

1. **All tests green first** — never ship broken code.
2. **Land the work via a feature branch + PR** — one branch per feature, open a PR, then squash-merge into `master`. **Do NOT push to `master` directly** (Claude Code auto mode blocks it anyway — see below).
3. **Docker rebuild and redeploy** — always `--no-cache` for any Angular/SCSS/TS change.

```powershell
# Step 1 — verify tests
npx ng test shared-lib --no-watch
npx ng test web        --no-watch
Set-Location backend; dotnet test; Set-Location ..

# Step 2 — feature branch → commit → PR → squash-merge
git switch -c feat/<name>
git add -A
git commit -m "your message here"
git push -u origin feat/<name>
gh pr create --base master --title "..." --body-file <body.md>
# after tests green + Docker rebuilt:
gh pr merge <n> --squash --delete-branch
git switch master; git pull origin master

# Step 3 — rebuild and redeploy Docker
docker compose build web --no-cache
docker compose up -d web
```

**Workflow:** feature branch → commit → PR → squash-merge into `master`. There is currently no production
deployment and the manual deploy workflow no longer exists, but `master` still only advances through merged PRs. Auto mode **blocks** `git push origin master`
(bypasses PR review) but allows feature-branch push, `gh pr create`, and `gh pr merge` — so the PR flow
is the path of least friction. (This supersedes the earlier "push to master" instruction.)

**Never leave Docker stale.** A stale Docker image means the running app doesn't match the source. If unsure whether Docker is current, rebuild — `--no-cache` takes a few minutes and is always safe.

**Never leave work unmerged.** All work must reach `origin/master` (via a merged PR) so it isn't lost and so teammates/CI can see it.

---

## MediatR pipeline behaviors

Every request sent through `IMediator.Send` passes through two open behaviors (registered in
`Program.cs`, in `Application/Common/Behaviors/`):

- **`LoggingBehavior`** — logs each request name + elapsed ms, and a warning (with exception) on failure.
- **`ValidationBehavior`** — runs any registered FluentValidation `IValidator<TRequest>` and throws
  `ArgumentException` (→ HTTP 400) on the first failure. **Opt-in:** a request is only validated if a
  validator exists for it. Existing commands keep their **inline** guards — the handler-direct unit
  tests (`handler.Handle(...)`) bypass the pipeline, so inline validation is what they exercise. To add
  boundary validation for a NEW command, drop a `class XCommandValidator : AbstractValidator<XCommand>`
  in the Application assembly — `AddValidatorsFromAssembly` discovers it automatically.

Note: because behaviors only run via `IMediator.Send`, they're exercised by the real app and the
`Integration.Tests` (WebApplicationFactory), not by handler-direct unit tests.

## Backend unit test pattern (C# xUnit + EF Core InMemory)

All backend tests live in `backend/tests/Application.Tests/`. Each test class creates a fresh in-memory `AppDbContext` per test to guarantee isolation.

### Test commands
```bash
cd backend
dotnet test tests/Application.Tests/               # run all Application tests (EF InMemory, fast)
dotnet test tests/Integration.Tests/               # real-Postgres tests (Testcontainers — needs Docker)
dotnet test tests/Infrastructure.Tests/            # run Infrastructure tests
dotnet test                                         # run everything
```

> **`tests/Integration.Tests/` requires Docker.** It spins up a throwaway `postgres:17-alpine`
> via Testcontainers to prove things EF InMemory cannot: optimistic concurrency (the `xmin`
> token), Serializable balance-overrun
> arbitration, and the HTTP pipeline (rate limiter + exception→status mapping via
> `WebApplicationFactory<Program>`). If Docker isn't available these tests fail to start — run
> the Application suite instead.
>
> `LoadConcurrencyTests` drives **50 concurrent** leave approvals vs a 10h cap (never
> overdraws, and reported successes EXACTLY equal committed rows). The last assertion guards a
> subtlety: `EnableRetryOnFailure` isn't transaction-aware, so a create that committed and was then
> retried used to re-check the now-full balance and falsely report "insufficient" while its row
> stayed committed. `UpsertLeaveCommand` fixes this with an **idempotency guard** — it pre-generates
> the leave Id, clears the change tracker at the start of each attempt, and on entry returns the
> already-committed row instead of re-inserting. `scripts/load-test-50-live.ps1` runs the
> rate-limiter burst against the live API.

### Auth hardening (login defense)
Two complementary layers guard `/api/auth`:
- **Per-account lockout** — `Staff.AccessFailedCount` / `LockoutEnd`; after `AppConstants.Lockout.MaxFailedAttempts` (5) consecutive failures the account locks for `Lockout.Duration` (15 min) and `LoginCommand` returns **429** (even for the correct password) until it expires; a success resets the counter.
- **Per-IP fixed-window rate limiter** — `[EnableRateLimiting("auth")]` on `AuthController`; overflow returns **429**. This is only a coarse anti-flood ceiling, NOT the brute-force control. Default **100/60s** per client IP, tunable via `RateLimit:AuthPermitLimit` / `RateLimit:AuthWindowSeconds` (env: `RateLimit__AuthPermitLimit`). It must clear a shift-change burst because a whole venue shares one wifi/NAT public IP. `Program.cs` adds `UseForwardedHeaders` (X-Forwarded-For) so behind nginx the limiter partitions by the REAL client IP, not the proxy's — without it the limit degenerates into one global bucket. Any single-source client that authenticates in bulk (e.g. the E2E suite) must reuse a token rather than logging in per request.

### Boilerplate
```csharp
using Microsoft.EntityFrameworkCore;
using HRNexus.Infrastructure.Persistence;
using Xunit;
using E = HRNexus.Domain.Entities;

namespace HRNexus.Application.Tests;

public class MyFeatureTests
{
    private static AppDbContext MakeDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())   // unique DB per test
            .Options;
        return new AppDbContext(opts);
    }
}
```

### Available stubs (defined in `TestHelpers.cs`)
| Stub | Replaces |
|---|---|
| `NullNotificationHub` | `INotificationHub` (no-op SignalR) |
| `NullPushSvc` | `IPushNotificationService` (no-op) |
| `NullAuditService` | `IAuditService` (no-op) |
| `StubHasher` | `IPasswordHasher` — `Hash(p)` returns `"hash:"+p`; `Verify(p,h)` returns `h == "hash:"+p` |
| `StubTokenService` | `ITokenService` — always returns a fixed token/refresh pair |
| `StubDateTimeProvider` | `IDateTimeProvider` — exposes a mutable `DateTime _now` field |

### Access is decided by permissions, never by a job's name (Phase 7o)

No code compares `Role.Name` to a constant, and there is no `SUPERADMIN` or `MANAGER`. A job is a name plus a set of
permissions. What replaces the names:

- **Who is notified** is a permission lookup: `PermissionHolders.GetIdsAsync(db, key, ct)` returns the active
  accounts that effectively hold a key (job grants, plus per-user grants, minus per-user revokes). Pending and
  withdrawn leave go to `leave.approve` holders; password-reset requests go to `staff.manage` holders. The person
  the notice is about (the leave's owner, the admin who recorded it, the person asking for a reset) is never told.
  A test that needs "someone who can approve leave" grants `leave.approve` to a job (`RoleSeed.GrantAsync`).
- **The first administrator job** is the one job flagged `IsSystem`. The seed finds it by the flag (creating
  `ADMINISTRATOR` on an empty install), grants it every catalog permission at every start, and `SetRolePermissions`
  refuses to edit it. It can be renamed, never deleted. Do not look it up by name.
- **Lock-out protection** is by permission: `RolesManageInvariant.EnsureNotTheLastAsync` stops the last active
  `roles.manage` holder from being deactivated or deleted.
- **`HRADMIN`** (documented first-run password) is created **only in an empty database**, counting deleted accounts,
  so deleting or renaming it can never bring the default password back.
- The access token carries no role claim and the frontend has no role tier: use `permissionGuard` and the
  permission service.

### Role.Name primary key casing — canonical form is UPPERCASE
`Role.Create(name)` stores `Name = name.Trim().ToUpperInvariant()`. All handlers query roles using `.ToUpperInvariant()` to match. In tests, `E.Role.Create("bartender")` already produces `Name = "BARTENDER"` — no reflection workaround needed:

```csharp
private static E.Role MakeRole(string name = "bartender") =>
    E.Role.Create(name);   // Name stored as "BARTENDER" — matches handler queries
```

### TDD cycle for backend
1. Read the handler's `Handle()` method fully — note every branch and validation
2. Write one failing test per branch (validation error, not-found, success)
3. Run `dotnet test` — confirm it fails for the right reason
4. Implement / fix — green
5. One test per logical branch; no duplicate assertions

---

## Frontend service test pattern (HttpTestingController)

For services that make HTTP calls — use `HttpTestingController`. Pattern:

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { MyService } from './my.service';

describe('MyService', () => {
  let service: MyService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [MyService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MyService);
    http    = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());   // fails if any request was not handled
});
```

**Key rules:**
- Use `http.expectOne(url)` for exact URL; `http.expectOne(r => r.url === '...')` when params vary
- Always `req.flush(data)` to resolve each pending request
- For mutation methods that call `tap(() => this.loadX())` internally, a second GET request fires automatically — always flush it too, or `http.verify()` will fail
- `urlWithParams` includes query string; check it with `.toContain('param=value')`
- Angular encodes spaces as `%20` (not `+`) in query params

---

## Coverage tracking (Rule 17)

After writing or modifying any handler/service/component, run the full coverage check to confirm nothing is left untested.

### Quick coverage scan (PowerShell — run from repo root)

```powershell
# ── Frontend: find .component.ts / .service.ts / .store.ts / .guard.ts with no spec ──
$roots = @(
  "projects/shared-lib/src/lib/services",
  "projects/shared-lib/src/lib/stores",
  "projects/shared-lib/src/lib/guards",
  "projects/shared-lib/src/lib/auth",
  "projects/web/src/app"
)
$gaps = @()
foreach ($root in $roots) {
  Get-ChildItem -Path $root -Recurse -Include "*.service.ts","*.store.ts","*.guard.ts","*.component.ts" |
    Where-Object { $_.Name -notmatch "\.spec\.ts$" } |
    ForEach-Object {
      $spec = $_.FullName -replace "\.ts$", ".spec.ts"
      if (-not (Test-Path $spec)) { $gaps += "MISSING SPEC: $($_.FullName)" }
    }
}
if ($gaps.Count -eq 0) { Write-Host "✓ No frontend spec gaps" -ForegroundColor Green }
else { $gaps | ForEach-Object { Write-Host $_ -ForegroundColor Red } }

# ── Backend: find *Command.cs / *Query.cs with no test coverage marker ──
# (Manual cross-reference — see "Baseline" table below)
```

### Run all tests (full baseline check)

```powershell
# From repo root
npx ng test shared-lib --no-watch
npx ng test web        --no-watch

# Backend
Set-Location backend
dotnet test
```

### Baseline — as of 2026-11-14 (0 gaps, all green)

| Layer | Test files | Tests | Status |
|---|---|---|---|
| shared-lib (services + stores + guards + auth) | 29 | 279 | ✅ |
| web components | 14 | 488 | ✅ |
| backend Application.Tests | — | 412 | ✅ |
| backend Integration.Tests (Testcontainers) | — | 35 | ✅ |
| backend Infrastructure.Tests | — | 10 | ✅ |
| E2E (Playwright) | 14 | 65 | ✅ |

**Mandate**: Before marking any feature "done", confirm the scan returns 0 gaps and all tests pass. Surface any skipped tests explicitly — do NOT mark complete with silent gaps.

---

## Component unit test pattern (Angular 21 standalone)

Mock all injected services; do NOT call `detectChanges()` — this avoids PrimeNG template errors and keeps tests focused on TypeScript logic only.

```typescript
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';

await TestBed.configureTestingModule({
  imports: [ComponentUnderTest],
  providers: [
    provideHttpClient(),
    provideRouter([]),
    { provide: SomeService, useValue: { method: vi.fn().mockReturnValue(of(data)) } },
    MessageService,       // always provide — PrimeNG requires these even in unit tests
    ConfirmationService,
  ],
}).compileComponents();

const component = TestBed.createComponent(ComponentUnderTest).componentInstance;
// Do NOT call detectChanges()
// Test signals and methods directly: component.mySignal(), component.myMethod()
```

**Key patterns:**
- Mock services with `vi.fn().mockReturnValue(of(value))` for success; `throwError(() => err)` for failure
- Store signals (`StaffStore.staff`, `StaffStore.roles`, etc.) are plain functions — mock as `vi.fn().mockReturnValue([])`
- Angular `computed()` with non-signal dependencies (mocks) evaluates once and caches — tests needing different values use fresh component instances via `TestBed.createComponent()`
- For debounce tests: `vi.useFakeTimers()` + `vi.advanceTimersByTime(ms)`; always `vi.useRealTimers()` in `afterEach`
- For time-of-day computed (`new Date().getHours()`): `vi.useFakeTimers()` + `vi.setSystemTime(new Date(...))` before creating the component
- `ConfirmService.confirm()` mock: `{ confirm: vi.fn().mockReturnValue(of(true)) }` for accept; `of(false)` for reject

**Critical: stub routes for components that navigate**  
Any component that calls `router.navigate(['/some-path'])` — e.g. on successful login or submit — will throw `NG04002: Cannot match any routes` if `provideRouter([])` has no matching route. Always add a `StubComponent` and list every route the component may navigate to:

```typescript
import { Component } from '@angular/core';
@Component({ template: '', standalone: true }) class StubComponent {}

// In providers:
provideRouter([
  { path: 'auth/set-password', component: StubComponent },
  { path: 'home',              component: StubComponent },
])
```

Known components that navigate and require stub routes:
| Component | Routes needed |
|---|---|
| web `LoginComponent` | `home` |

---

## Development Process

All non-trivial work follows a phased, TDD-driven execution model. Phase documents live in `docs/process/`.

### Phased Execution Mandate (Rule 13)

Before implementing any significant feature or refactor:
1. **Brainstorm** — explore the problem space; no code yet
2. **Finalize spec** — write or update the phase document (`docs/process/PHASE_TEMPLATE.md`)
3. **Split into phases** — each phase ≤ 1–2 days of focused work; smaller is safer
4. **Execute one phase** — implement with TDD (Rule 14); never mix two phases in one pass
5. **Multi-perspective review** — use `REVIEW_CHECKLIST.md`; all 7 perspectives
6. **Update future phases** — adjust subsequent phase docs based on review findings
7. **Repeat** — next phase starts only after current phase is signed off

Do NOT attempt to implement everything at once. Doing so increases the risk of overlooking details and causing inconsistencies.

### TDD Enforcement (Rule 14)

**Red → Green → Refactor — strictly in that order.**

For every new handler, service method, or component behaviour:

1. **Write the failing test first** — name it for the *intent*, not the implementation
2. **Run it** — confirm it fails for the right reason (not a compile error, a real assertion failure)
3. **Write the minimum code** to make it pass — no extras
4. **Refactor** — keep all tests green throughout

Tests must verify **intent** (why the behavior matters), not just current output (Rule 9).  
A test that passes even when the business logic is deleted is wrong.

#### Which pattern to use

| What you're writing | Pattern section to follow |
|---|---|
| C# handler (command/query) | **Backend unit test pattern** |
| Angular service / store HTTP calls | **Frontend service test pattern** |
| Angular component logic (signals, methods) | **Component unit test pattern** |

#### Coverage check after every feature

Run the PowerShell scan in **Coverage tracking (Rule 17)** before closing any PR.  
The check must return 0 gaps and all test counts must be ≥ baseline.

**Test commands:**
```powershell
# Frontend (from repo root)
npx ng test shared-lib --no-watch   # 279 tests
npx ng test web        --no-watch   # 488 tests

# Backend
Set-Location backend
dotnet test                          # 457 tests (412 Application + 35 Integration + 10 Infrastructure)
dotnet test tests/Application.Tests/
dotnet test tests/Infrastructure.Tests/
```
```

### Multi-Perspective Review (Rule 15)

After every phase, reviews must cover all 7 perspectives in `docs/process/REVIEW_CHECKLIST.md`:
1. Architecture
2. Scalability
3. Security
4. Maintainability
5. Testing
6. Product Alignment
7. Performance

Issues found during review → update the next phase's scope/risks before proceeding.

### E2E Validation (Rule 16)

The final phase of any project must include E2E validation that covers:
- The primary happy-path user journey end-to-end
- At least one edge case per major feature
- Auth flow (login → guarded route) for any route changes

E2E tests live in `tests/e2e/{admin,staff}/` and use Playwright. There is **one** Playwright project, `web`
(Desktop Chrome); both folders run in it. The folders only group scenarios by the account that runs them:
`admin/` signs in as the administrator, `staff/` as a plain employee.
Run against the Docker deployment (`BASE_URL`, default `http://localhost:8080`).

**Seeded test credentials (administrator):** staffId=`HRADMIN`, password=`Admin@1234` (holds the first administrator job, so every permission)  
**E2E employee account (created by global-setup):** staffId=`TESTSTAFF01`, password=`Test@1234` (EMPLOYEE role, no management permissions)

**Run commands:**
```bash
npm run test:e2e          # all tests (both folders, one `web` project)
npm run test:e2e:ui       # interactive Playwright UI
PW_CHANNEL=msedge npm run test:e2e   # use the installed Edge/Chrome when Playwright's bundled Chromium cannot start (PowerShell: $env:PW_CHANNEL='msedge')
```

**Current E2E specs:**
| File | Covers |
|---|---|
| `tests/e2e/admin/auth.spec.ts` | Login, wrong password, authGuard redirect, logout |
| `tests/e2e/admin/dashboard.spec.ts` | Redirect after login, total-staff KPI card, live/connecting badge |
| `tests/e2e/admin/staff.spec.ts` | Staff list renders, search, detail navigation, bulk mode |
| `tests/e2e/admin/roles.spec.ts` | Roles page renders, role table, Add role dialog |
| `tests/e2e/admin/leave.spec.ts` | Leave page, filters, Record Leave dialog, summary pills |
| `tests/e2e/admin/policy.spec.ts` | Company policy: employee reads but cannot save, invalid and stale saves refused, Mon-Sat switch on the page makes Saturday leave valid (then restored) |
| `tests/e2e/admin/permissions.spec.ts` | Permission-gated pages, per-user grant/revoke override, 403 vs 200 on a protected endpoint |
| `tests/e2e/admin/negative.spec.ts` | API guards reject cleanly: overlap, bad dates, hours cap, blocked deletes, duplicate Staff ID, 401s |
| `tests/e2e/admin/workflow.spec.ts` | Hire staff, staff applies for leave, admin approves, read models reflect it |
| `tests/e2e/staff/auth.spec.ts` | Login → /home, wrong password, authGuard, logout |
| `tests/e2e/staff/home.spec.ts` | Greeting, sidebar navigation, leave balance cards |
| `tests/e2e/staff/my-leave.spec.ts` | Balances, Apply/History tabs, application form validation |
| `tests/e2e/staff/profile.spec.ts` | Profile card, personal details, push toggle, sign-out |
