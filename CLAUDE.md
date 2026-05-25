# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Project overview

Angular 21 + PrimeNG 21 monorepo. Two frontend apps share one backend and one library:

| Project | Path | Audience | Notes |
|---|---|---|---|
| `admin-app` | `projects/admin-app` | Venue managers | Desktop-first SPA |
| `staff-app` | `projects/staff-app` | Venue staff | Mobile-first PWA (service worker) |
| `shared-lib` | `projects/shared-lib` | Both apps | Models, services, stores, guards, pipes, base components |
| .NET API | separate repo | Backend | `/api/` proxied by nginx |

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
npx ng serve admin-app    # → localhost:4200
npx ng serve staff-app    # → localhost:4201
```

After every deploy, hard-refresh the browser (**Ctrl+Shift+R**) — nginx caches `index.html` without a content hash.

> **Rule 18**: After any coding session, always commit to GitHub AND rebuild Docker before closing.  
> See **Rule 18 — Docker and GitHub must always reflect the latest code** in the Process rules section.

---

## Project structure

```
projects/
├── admin-app/src/app/
│   ├── app.ts                    # root component (<router-outlet />)
│   ├── app.config.ts             # providers (HttpClient, PrimeNG, i18n, guards)
│   ├── app.routes.ts             # all routes
│   ├── shell/                    # layout shell (sidebar, topbar, nav)
│   └── features/                 # one folder per route page
│       ├── dashboard/
│       ├── staff/                # staff list + staff-detail
│       ├── leave/
│       ├── overtime/
│       ├── payroll/
│       ├── tips/
│       ├── reports/
│       ├── roles/
│       ├── locations/
│       ├── announcements/
│       ├── feature-flags/
│       ├── staff-ledger/         # per-staff earnings ledger (featureFlag: staffLedger)
│       ├── staff-settings/       # work types & settlements config (featureFlag: staffLedger)
│       └── account/
│
├── staff-app/src/app/
│   ├── (same shell + app structure)
│   └── features/
│       ├── home/
│       ├── log-shift/
│       ├── pay-summary/
│       ├── my-leave/
│       ├── tips-history/
│       ├── announcements/
│       └── profile/
│
└── shared-lib/src/lib/
    ├── models/                   # DTOs and interfaces
    ├── services/                 # HTTP services + LangService + ToastService + ConfirmService
    ├── stores/                   # signal-based state (StaffStore, LocationStore, …)
    ├── guards/                   # authGuard, roleGuard, featureFlagGuard, featurePinGuard
    ├── auth/                     # AuthService + jwtInterceptor
    ├── pipes/                    # AmPmPipe
    ├── directives/               # PickerOnlyDirective
    └── components/               # ToastComponent, ConfirmDialogComponent, NotificationBellComponent,
                                  # ThemeToggleComponent, ChangePasswordComponent
```

Feature files follow the pattern: `{name}.component.ts`, `.html`, `.scss` (and `.routes.ts` for sub-routes).

---

## Angular setup

- **Fully standalone components — no NgModules anywhere.**
- Bootstrap: `main.ts` → `bootstrapApplication(App, appConfig)`
- Root `App` component contains only `<router-outlet />`
- All feature routes are lazy-loaded via `loadComponent()` / `loadChildren()`

**`app.config.ts` key providers:**
```typescript
provideRouter(routes, withComponentInputBinding())
provideHttpClient(withInterceptors([jwtInterceptor]))
provideTranslateService({ defaultLanguage: 'en' })
providePrimeNG({ theme: { preset: Aura, options: { darkModeSelector: '[data-theme="dark"]' } } })
MessageService, ConfirmationService   // PrimeNG services
// staff-app only:
provideServiceWorker('ngsw-worker.js', { enabled: isDevMode() === false })
```

---

## Routing

All routes in `app.routes.ts`. Pattern: lazy load + guards.

**Admin routes (selected):**
```
/dashboard             authGuard + roleGuard('manager','admin')
/staff                 same
/staff/:id             same (StaffDetailComponent)
/leave                 same
/payroll               same + featureFlagGuard('payroll', '/dashboard')
/tips                  same + featureFlagGuard('tips', '/dashboard')
/announcements         same + featureFlagGuard('announcements', '/dashboard')
/reports               same
/staff-ledger          authGuard + roleGuard + featureFlagGuard('staffLedger', '/dashboard')
/staff-settings        authGuard + roleGuard (work types & settlements admin)
/roles, /locations     same
/features/unlock       featurePinGuard
/features              featurePinGuard + admin-only
/profile               redirectTo: '/account'
/auth/**               login, forgot-password, set-password (public)
```

**Staff routes (selected):**
```
/home                  authGuard
/log-shift             authGuard
/my-leave              authGuard
/pay-summary           authGuard + featureFlagGuard('paySummary', '/home')
/tips-history          authGuard + featureFlagGuard('tipsHistory', '/home')
/profile/**            authGuard (sub-routes: profile, change-password)
/auth/**               public
```

**Guards:**
- `authGuard` — checks `auth.isAuthenticated() && auth.hasValidToken()`
- `roleGuard(...roles)` — checks `auth.userRole()` is one of the given roles
- `featureFlagGuard(key, fallback)` — triggers `featureSvc.load()` on fresh load; falls back to `fallback` route if disabled
- `featurePinGuard` — checks `sessionStorage['nc_feature_token']`

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

**Error handling:** 401s are intercepted (see jwtInterceptor). All other errors propagate to the subscriber's `error` callback. Components call `this.toast.error(err?.error?.error ?? 'Generic error message.')`.

**No `environment.ts` files.** All config is either hardcoded relative URLs or fetched from the API at runtime.

---

## Authentication

**AuthService** (`shared-lib/src/lib/auth/auth.service.ts`):

```typescript
// Key signals
_user = signal<AuthUser | null>(...)     // loaded from localStorage on init
isAuthenticated = computed(...)          // _user() !== null
userRole = computed(...)                 // 'admin' | 'manager' | 'employee' | null

// Key methods
login(req: LoginRequest): Observable<AuthResponse>
logout(): void                           // clears localStorage, navigates to /auth/login
refreshToken(): Observable<AuthResponse>
changePassword(req): Observable
forceChangePassword(pwd: string): Observable   // mustChangePassword flow
forgotPassword(staffId: string): Observable<{ found: boolean }>
hasRole(...roles: string[]): boolean
hasValidToken(): boolean                 // checks JWT exp claim
getAccessToken(): string | null
```

**Token storage:**
- `localStorage['access_token']` — JWT
- `localStorage['refresh_token']`
- `localStorage['auth_user']` — JSON of `AuthUser`
- `sessionStorage['nc_feature_token']` — feature-flags PIN unlock token

**mustChangePassword flow:**
1. Login endpoint returns `{ mustChangePassword: true }` in `AuthResponse`
2. Auth service stores it; login component redirects to `/auth/set-password`
3. `SetPasswordBase` (shared-lib base class) handles form + `forceChangePassword()` call + redirect

**jwtInterceptor** (`shared-lib/src/lib/auth/jwt.interceptor.ts`):
- Attaches `Authorization: Bearer {token}` to every request
- On 401: queues concurrent requests, silently refreshes token, replays
- On refresh failure: calls `auth.logout()`
- Skips refresh logic for requests to `/api/auth/` to prevent loops

---

## Feature flags

**FeatureFlagService** (`shared-lib/src/lib/services/feature-flag.service.ts`):

```typescript
type FeatureKey = 'payroll' | 'tips' | 'tipsHistory' | 'paySummary' | 'announcements' | 'staffLedger'

// Signals
flags = signal<Record<FeatureKey, boolean>>(defaults)   // all false by default
loaded = signal(false)

// Methods
load(): void                    // GET /api/feature-flags (called by guard + shell)
isEnabled(key): boolean
toggle(key): Observable         // PUT /api/feature-flags  (admin only)
enableAll() / disableAll()
```

To check a flag in a component:
```typescript
private readonly featureSvc = inject(FeatureFlagService);
isPayrollEnabled = computed(() => this.featureSvc.isEnabled('payroll'));
```

---

## Shared-lib exports

**Models** (key ones): `Staff`, `StaffRole`, `RegisterStaffRequest`, `UpdateStaffRequest`, `AuthUser`, `AuthResponse`, `LoginRequest`, `LeaveRequest`, `LeaveBalance`, `NotificationDto`, `AnnouncementDto`, `AdminAnnouncementDto`, `UpsertAnnouncementDto`, `Location`, `PayrollData`, `WorkLogDto`, `PagedResult<T>`, `StaffEntryDto`, `StaffExpenseDto`, `StaffLoanDto`, `StaffLedgerPayoutDto`, `StaffOutstandingDto`, `StaffWorkTypeDto`, `StaffSettlementDto`

**Services:**
| Service | Purpose |
|---|---|
| `AuthService` | Login, logout, token refresh, password change |
| `StaffManagementService` | Employee CRUD (get, register, update, toggleActive, getRoles) |
| `StaffService` | Staff ledger: entries (CRUD), expenses (CRUD), loans (create/delete), `summary(year,month)`, `outstanding()`, `bulkSettle(entryIds, settlement)` |
| `StaffConfigService` | Work types & settlements — `/api/staff-config` |
| `WorkLogService` | Shift log CRUD; exports `PagedResult<T>` |
| `PayrollService` | Payroll data |
| `LeaveService` | Leave requests + balances |
| `NotificationService` | Notification list + unread count |
| `AnnouncementService` | Announcement CRUD |
| `MessagingService` | Message send/receive |
| `SignalRService` | WebSocket connection to `/hubs/` |
| `LangService` | Language toggle (see i18n section) |
| `ToastService` | `success/error/warning/info(msg)` |
| `ConfirmService` | `confirm(opts) → Observable<boolean>` |
| `ThemeService` | Dark/light toggle |
| `FeatureFlagService` | Feature flag queries + toggle |

**Guards:** `authGuard`, `roleGuard`, `featureFlagGuard`, `featurePinGuard`

**Components:**
- `ToastComponent` (`<nc-toast />`) — mount once in each app shell
- `ConfirmDialogComponent` (`<nc-confirm-dialog />`) — mount once in each app shell
- `NotificationBellComponent` — topbar bell with popover
- `ThemeToggleComponent` — dark/light button
- `ChangePasswordComponent` — reusable password change form

**Base classes:**
- `SetPasswordBase` — logic for forced-password-change pages (both apps extend this)
- `ForgotPasswordBase` — logic for forgot-password pages

**Pipes:** `AmPmPipe` — `"20:30"` → `"8:30 PM"`, `null` → `"—"`

**Directives:** `PickerOnlyDirective` — disables keyboard on date inputs on mobile

**Utilities:** `provideNoAutofill()`, date utils

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

Available stores: `StaffStore`, `LocationStore` (confirmed). Other domains use service classes directly.

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

Both components (`<nc-toast />` and `<nc-confirm-dialog />`) must be in each app's shell template. They are already there — do not add them again.

---

## Form pattern

**ngModel only — no reactive forms anywhere.** Forms are plain object + template bindings.

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

Rule: use `<p-skeleton>` for initial page loads (>150 ms). Use `[loading]="saving()"` on PrimeNG buttons for action waits. Use `<p-progressspinner>` only for section-level reloads (e.g. paginating).

---

## i18n

**Language support:** English (`en`) and Chinese Simplified (`zh`).

**File locations:**
```
projects/admin-app/public/i18n/en.json
projects/admin-app/public/i18n/zh.json
projects/staff-app/public/i18n/en.json
projects/staff-app/public/i18n/zh.json
```

**Key structure (namespaced, flat values):**
```json
{
  "nav":   { "staff": "Staff", "leave": "Leave" },
  "auth":  { "staffId": "Staff ID", "signIn": "Sign In" },
  "common":{ "save": "Save", "cancel": "Cancel", "active": "Active" },
  "staff": { "addStaff": "Add Staff", "fullName": "Full Name" }
}
```

Always add keys to **both** `en.json` and `zh.json` when adding any new UI text.

**In templates:** `{{ 'key.sub' | translate }}`

**In components:**
```typescript
private readonly translate = inject(TranslateService);
const msg = this.translate.instant('staff.saved');
```

**LangService** (`shared-lib`):
```typescript
readonly current = signal<'en' | 'zh'>('en');   // persisted to localStorage
toggle(): void                                   // en ↔ zh
set(lang, syncToApi = true): void                // updates signal + localStorage + PATCH /api/auth/profile
```

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

Tokens resolve to CSS custom properties (`var(--nc-bg-surface)` etc.). Dark mode is `:root` default; light mode overrides live under `[data-theme="light"]` in `styles.scss`.

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
1. Node 22 builder: `npm ci` → build `shared-lib` → build `admin-app` → build `staff-app`
2. nginx alpine: copies both `dist/` outputs, serves on ports 80 (admin) and 8081 (staff)

**nginx behaviour:**
- `try_files $uri $uri/ /index.html` — SPA client-side routing
- `/api/` → proxied to `http://api:8080`
- `/hubs/` → proxied with WebSocket upgrade headers (SignalR)
- `/uploads/` → proxied to API (media files)
- `index.html` and `/i18n/` — `no-cache` headers so new deploys take effect immediately

**No `environment.ts` files.** All config is relative URLs or runtime API calls.

### UAT deployment (soft launch for user testing)

Uses `docker-compose.uat.yml` overlay which overrides CORS to accept the public server IP/hostname instead of prod subdomains.

**Required `.env` file on the server** (never commit this):
```
DOMAIN=yourname.duckdns.org
UAT_HOST=yourname.duckdns.org
JWT_SECRET=<random-long-string-min-32-chars>
```

**Launch command on UAT server:**
```bash
docker compose -f docker-compose.yml -f docker-compose.uat.yml up -d --build
```

**Update after pushing new code:**
```bash
git pull && docker compose -f docker-compose.yml -f docker-compose.uat.yml up -d --build --no-deps web api
```

**UAT URLs** (replace `yourname` with your DuckDNS subdomain):
- Admin portal: `http://yourname.duckdns.org` (port 80)
- Staff portal: `http://yourname.duckdns.org:8081`

**Server spec**: Minimum **2 GB RAM** (DigitalOcean $14/month Singapore droplet) — the Angular build needs ~1.5 GB during `docker compose build`.

**Firewall**: Open ports 80, 8081, and 22 with `ufw allow 80 && ufw allow 8081 && ufw allow 22 && ufw enable`.

---

## Staff pay model — design intent

Two systems exist and intentionally serve different purposes. Do not conflate them:

| System | Where | Purpose |
|---|---|---|
| **Shift logs** | `WorkLogService`, `/api/work-logs`, staff-app `log-shift`, admin Reports workforce tab | Attendance & compliance — who was physically present, clock-in/out times, shift duration |
| **Staff ledger** | `StaffService`, `/api/staff-entries`, admin-app `staff-ledger` | Actual pay — commission earnings by work type (写钟, 跑台, 包夜, etc.), deductions, loan advances/repayments, settlement status |

**All staff are commission-based** (not hourly). The "Est. Labour Cost" column in the workforce report is a rough estimate only — the ledger is the financial source of truth for payouts.

The two systems are intentionally decoupled. A shift log proves presence; a ledger entry records earnings. Managers enter ledger entries manually after each period.

---

## Design reference

`docs/design.md` — complete design system spec: color tokens, component states, screen wireframes, gap list (U1–U30), migration plan.

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

After any session that modifies source files, spec files, config, or migrations:

1. **All tests green first** — never ship broken code.
2. **Commit everything to GitHub** — staged, committed, pushed to `origin/master`.
3. **Docker rebuild and redeploy** — always `--no-cache` for any Angular/SCSS/TS change.

```powershell
# Step 1 — verify tests
npx ng test shared-lib --no-watch
npx ng test admin-app  --no-watch
npx ng test staff-app  --no-watch
Set-Location backend; dotnet test; Set-Location ..

# Step 2 — commit and push
git add -A
git commit -m "your message here"
git push origin master

# Step 3 — rebuild and redeploy Docker
docker compose build web --no-cache
docker compose up -d web
```

**Never leave Docker stale.** A stale Docker image means the running app doesn't match the source. If unsure whether Docker is current, rebuild — `--no-cache` takes a few minutes and is always safe.

**Never leave commits unpushed.** All work must be on `origin/master` so it isn't lost and so teammates/CI can see it.

---

## Backend unit test pattern (C# xUnit + EF Core InMemory)

All backend tests live in `backend/tests/Application.Tests/`. Each test class creates a fresh in-memory `AppDbContext` per test to guarantee isolation.

### Test commands
```bash
cd backend
dotnet test tests/Application.Tests/               # run all Application tests
dotnet test tests/Infrastructure.Tests/            # run Infrastructure tests
dotnet test                                         # run everything
```

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

### Role.Name primary key casing — canonical form is UPPERCASE
`Role.Create(name)` stores `Name = name.Trim().ToUpperInvariant()`. All handlers query roles using `.ToUpperInvariant()` to match. In tests, `E.Role.Create("bartender")` already produces `Name = "BARTENDER"` — no reflection workaround needed:

```csharp
private static E.Role MakeRole(string name = "bartender", decimal rate = 20m) =>
    E.Role.Create(name, hourlyRate: rate);   // Name stored as "BARTENDER" — matches handler queries
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
  "projects/admin-app/src/app",
  "projects/staff-app/src/app"
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
npx ng test admin-app  --no-watch
npx ng test staff-app  --no-watch

# Backend
Set-Location backend
dotnet test
```

### Baseline — as of 2026-05-22 (0 gaps, all green)

| Layer | Test files | Tests | Status |
|---|---|---|---|
| shared-lib (services + stores + guards + auth) | 25 | 360 | ✅ |
| admin-app components | 22 | 685 | ✅ |
| staff-app components | 12 | 245 | ✅ |
| backend Application.Tests | — | 401 | ✅ |

**Wrapper-component rule**: `change-password-page.component.spec.ts` in both apps intentionally has 1 smoke test — the component is a pure template wrapper around shared-lib's `ChangePasswordComponent`; all logic lives in shared-lib and is tested there.

**7 future feature areas** (no source yet → no tests needed yet):  
`Clock`, `Incidents`, `QrCodes`, `Roster`, `ShiftSwap`, `ShiftTemplates`, `VenueEvents`  
Each must get a test file written **at the same time** its first handler is implemented.

**Mandate**: Before marking any feature "done", confirm the scan returns 0 gaps and all tests pass. Surface any skipped tests explicitly — do NOT mark complete with silent gaps.

---

## Component unit test pattern (Angular 21 standalone)

Mock all injected services; do NOT call `detectChanges()` — this avoids PrimeNG template errors and keeps tests focused on TypeScript logic only.

```typescript
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MessageService, ConfirmationService } from 'primeng/api';

await TestBed.configureTestingModule({
  imports: [ComponentUnderTest],
  providers: [
    provideHttpClient(),
    provideRouter([]),
    provideTranslateService({ defaultLanguage: 'en' }),
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
- Store signals (`StaffStore.staff`, `LocationStore.locations`, etc.) are plain functions — mock as `vi.fn().mockReturnValue([])`
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
  { path: 'dashboard',        component: StubComponent },
  { path: 'auth/set-password', component: StubComponent },
  { path: 'home',              component: StubComponent },
])
```

Known components that navigate and require stub routes:
| Component | Routes needed |
|---|---|
| admin-app `LoginComponent` | `dashboard`, `auth/set-password` |
| admin-app `SetPasswordComponent` | `dashboard` |
| admin-app `FeaturePinEntryComponent` | `features` |
| staff-app `LoginComponent` | `home`, `auth/set-password` |
| staff-app `SetPasswordComponent` | `home` |

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
npx ng test shared-lib --no-watch   # 360 tests
npx ng test admin-app  --no-watch   # 685 tests
npx ng test staff-app  --no-watch   # 245 tests

# Backend
Set-Location backend
dotnet test                          # 401 tests
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

E2E tests live in `tests/e2e/{admin,staff}/` and use Playwright.
Run against the Docker deployment (`http://localhost` for admin, `http://localhost:8081` for staff).
