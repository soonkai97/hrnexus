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

```bash
# Deploy (Docker) — always --no-cache for SCSS/TS changes
# Without it Docker reuses the cached Angular build layer and changes won't appear
docker compose build web --no-cache && docker compose up -d web

# Incremental deploy (only when you know the layer is already stale)
docker compose build web && docker compose up -d web

# Local dev (no Docker) — proxy.conf.json routes /api/ to localhost API
npx ng serve admin-app    # → localhost:4200
npx ng serve staff-app    # → localhost:4201
```

After every deploy, hard-refresh the browser (**Ctrl+Shift+R**) — nginx caches `index.html` without a content hash.

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
│       ├── messaging/
│       ├── notifications/
│       ├── feature-flags/
│       └── account/
│
├── staff-app/src/app/
│   ├── (same shell + app structure)
│   └── features/
│       ├── home/
│       ├── log-shift/
│       ├── pay-summary/
│       ├── my-leave/
│       ├── messaging/
│       ├── tips-history/
│       ├── announcements/
│       ├── notifications/
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
/overtime              same + featureFlagGuard('otRules', '/dashboard')
/payroll               same + featureFlagGuard('payroll', '/dashboard')
/tips                  same + featureFlagGuard('tips', '/dashboard')
/announcements         same + featureFlagGuard('announcements', '/dashboard')
/messaging             same + featureFlagGuard('messagingAdmin', '/dashboard')
/reports               same
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
/messaging             authGuard + featureFlagGuard('messagingStaff', '/home')
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
type FeatureKey = 'otRules' | 'payroll' | 'tips' | 'tipsHistory' | 'paySummary'
               | 'messagingAdmin' | 'messagingStaff' | 'announcements'

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

**Models** (key ones): `Staff`, `StaffRole`, `RegisterStaffRequest`, `UpdateStaffRequest`, `AuthUser`, `AuthResponse`, `LoginRequest`, `LeaveRequest`, `LeaveBalance`, `NotificationDto`, `AnnouncementDto`, `AdminAnnouncementDto`, `UpsertAnnouncementDto`, `Location`, `PayrollData`, `WorkLogDto`, `PagedResult<T>`

**Services:**
| Service | Purpose |
|---|---|
| `AuthService` | Login, logout, token refresh, password change |
| `StaffService` | Staff CRUD (some ops moved to `StaffStore`) |
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
