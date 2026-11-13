# Architecture alignment: HRNexus to the IMS standard

> **Status:** `Draft` (analysis only, nothing changed in code)
> **Created:** 2026-11-13 | **Depends on:** Phase 7 (remove shift log)
> **Reference:** github.com/soonkai97/IMS (`docs/architecture/{backend,frontend,data-and-performance-rules}.md`, ADRs, `.github`, `.githooks`, `tests/`, `web/`)

IMS is the owner's personal code standard. **Default for every IMS convention is ADOPT.** Only warehouse/stock-domain mechanics and
SQL Server specifics are excluded (section 2), each with a reason. Sizes: S ≤ 1 day, M 1-2 days, L 3-5 days (split into phases), XL > 1 week (split, per feature).

## 1. Gap table (IMS convention → HRNexus today)

### Backend

| Area | IMS convention | HRNexus today | Gap |
|---|---|---|---|
| Layering | Domain / Application / Infrastructure / Api; Api references Infrastructure only for DI; guarded by Architecture.Tests | Same four projects (`backend/src/*`); Api references Infrastructure; **no architecture tests** | S (rules missing, layout right) |
| CQRS entry | Own `IDispatcher`, `ICommand/IQuery/IRequestHandler`, behaviours Logging → Validation → handler; handlers never call each other | MediatR 12.4.1 (`IMediator.Send`), `LoggingBehavior` + `ValidationBehavior` (same pipeline shape), 28 request types, controllers inject `IMediator` | M mechanical (L total, per feature); also removes MediatR dependency |
| Vertical slices | `Features/<F>/UseCases/<UseCase>/{Command,Handler,Validator}`, `Specifications`, `Contracts`, `Services`; one public type per file | `Application/<Area>/Commands|Queries/*.cs`, several types per file, DTOs inline in commands | L (per feature, when touched) |
| Persistence | `IRepository<T>`/`IReadRepository<T>` + Ardalis specifications, `IUnitOfWork.ExecuteAsync` (transaction + retry + audit) | Handlers use `IAppDbContext` directly; no repositories, no specifications | L (per feature) |
| Domain behaviour | Private setters, factories, state methods, pure policies unit-tested without DB | Already the style (`Staff.Create`, `SetLocations`, `Deactivate`) | S (extract policies as modules grow) |
| Validation | FluentValidation in pipeline, validator per use case + shared request validators | FluentValidation 11, 11 validators, opt-in; most guards are inline (`ArgumentException`) | M (move inline guards to validators per feature) |
| Errors | `ApiExceptionHandler` → RFC 7807 ProblemDetails + stable `ProblemCodes` + `errors` map; FE maps by `code` | `ExceptionMiddleware` maps exception type → `{ error: "text" }`; no codes | M (breaking for FE + E2E: do in one phase) |
| Ids on the wire | Random `PublicId` GUID + int clustered key; opaque ids; out-of-scope = 404 | **Every entity already has a random `Guid` Id** (`Guid.NewGuid()`), exposed as-is | S for intent (see Q1); the int+PublicId hybrid is XL and not needed on PostgreSQL |
| Pagination | Offset `PagedQuery` for bounded grids; opaque encrypted seek `cursor` for lists that grow | Offset only (`PagedResult(Items, Total)`) | M (only audit/notification-like lists) |
| Auth + permissions | Permission catalog in code, roles in DB, `perm` claims in a 5-min token, one policy per permission, `NoRoleDecisionTests` | Permission catalog + `PermissionPolicyProvider` + roles in DB already; 30-min JWT in httpOnly cookie, refresh rotation | S-M (add role-decision test, shorten token lifetime decision) |
| API contract | OpenAPI at `/openapi/v1.json`, `Tag_action` operation ids, string enums, UTC `Z`, `WireJson`, contract tests | Swashbuckle 7 `AddSwaggerGen`, defaults; hand-written FE services | M |
| Concurrency | `If-Match`/`ETag` with keyed-hash token, 428/409, retry in `ExecuteAsync` | xmin optimistic concurrency, generic "refresh and try again" 409 | M (first user: payroll runs, Phase 8) |
| Module switches (feature management) | **ADOPT** (owner request 2026-11-13). `ModuleCatalog` in code, `Module.<code>` setting rows (missing = off), `[RequiresModule]` + `ModuleGateMiddleware` (404 before authorization), `modules.manage`, audited flip with reason, permission hiding, FE `ModulesStore`/`requireModule` | None. Old flag system removed (admin toggle UI + PIN `cb4e7cd` 2026-05-25; whole system `e49b66d` 2026-06-01: collapsed to one flag, drifted gates, no audit) | M (Phase 7h) |
| Logging / correlation | Correlation-id middleware, log templates, Serilog | `CorrelationIdMiddleware`, Serilog + Seq + OTel, `HttpContextEnricher` | S (already aligned) |
| Security headers | API `SecurityHeadersMiddleware` | Set at nginx edge (CLAUDE.md forbids duplicating at API) | none, keep nginx |
| Config constants | `ApiConfigKeys`, `ApiRoutes`, `HeaderNames`, `RouteSegments`, `Limits`, no magic strings | `AppConstants` in Domain; routes/headers are literals | S-M |
| DI hygiene | `ValidateScopes` + `ValidateOnBuild`, options validated at start-up | `AddInfrastructure(config)`; no `AddApplication`/`AddPresentation`, no ValidateOnBuild | S |
| Tests | `Domain.Tests` (pure), `Architecture.Tests`, `Api.Tests` (real DB, role matrix), smoke, load, security | `Application.Tests` (EF InMemory, 20 files), `Integration.Tests` (Testcontainers, 10), `Infrastructure.Tests` (2) | M (add Architecture + Domain test projects; keep existing) |
| Code style | `.editorconfig` C# rules (file-scoped ns, naming), `Directory.Build.props` | `.editorconfig` (TS only), `backend/Directory.Build.props` (nullable as errors, analyzers) | S |

### Frontend

| Area | IMS convention | HRNexus today | Gap |
|---|---|---|---|
| Workspace | Nx monorepo, one app, everything in `libs/` via `@ims/*` aliases | Angular CLI workspace, 3 projects (`admin-app`, `staff-app`, `shared-lib`), imports `'shared-lib'` | L |
| Tiers + boundaries | shared / core / features (stores, no pages) / containers (routed pages), `@nx/enforce-module-boundaries` fails lint | admin/staff `features/<page>` hold pages + data access; shared-lib is a flat bag (services, stores, models) | XL (incremental; new code first) |
| State | Signal stores + `QueryLoader` / `ApiCommand` (uniform loading/error/empty), `createPageState`, no NgRx | Signal stores (`StaffStore`, `LocationStore`) for 2 domains; other domains call services/HttpClient with ad-hoc `loading` signals | M-L |
| API client | NSwag-generated client committed from OpenAPI, `withIfMatch` | Hand-written services, hand-written models | L (needs 7e first) |
| UI kit / theme | No component library; `app-*` kit in `shared/ui`, role-named tokens, WCAG contrast check script | PrimeNG 21 + Aura + custom neon SCSS tokens | XL if PrimeNG is replaced; M if wrapped (Q2) |
| i18n | Runtime switch, typed message keys, catalog-completeness test | ngx-translate, 3 locale files per app, no completeness test | S-M (add completeness test) |
| Errors/HTTP | Interceptor chain: error mapping (ProblemDetails→`ApiError`), correlation id, credentials, CSRF, If-Match, timeout | `jwtInterceptor`, `maintenanceInterceptor`, `error.interceptor`; reads `err.error.error` | M (with 7d) |
| Lint / rules | ESLint + Stylelint + Prettier, husky pre-commit, banned patterns (autofocus, literal motion) | ESLint (angular-eslint) + Prettier; no Stylelint, no hooks | S |
| Testing | Jest/Spectator per lib, Playwright e2e | Vitest per project (shared-lib 310, admin 454, staff ~219), Playwright | S (keep Vitest, Q4) |
| PWA / SW | App-shell only, no API `dataGroups`, kill-switch worker | Service worker; `ngsw-config` not reviewed for dataGroups | S (audit) |

### Repo process

| Area | IMS | HRNexus today | Gap |
|---|---|---|---|
| Git hooks | `.githooks/{pre-commit,commit-msg,pre-push}` → `scripts/guard/*.mjs` (+ node tests) | none | S |
| CI | `ci.yml` (build `-warnaserror`, unit + architecture, integration on SQL service, smoke), `codeql`, `dast`, `e2e`, `release`, `deploy`, `pr-guard`, dependabot | one `ci.yml` (lint, build, unit tests; backend build + test) | M |
| PR hygiene | PR template with checklist + reviewer roles, `CODEOWNERS`, rulesets, conventional titles | none | S |
| Docs | `docs/architecture` (backend, frontend, ADRs), `docs/pm`, `docs/qa`, `docs/reviews`, `docs/team`, `TEMPLATE.md`, `TASKS.md` | `docs/process` (phases), `docs/team` (ported), `DEPLOY.md`, CLAUDE.md, STATE.md | S |
| Agent team | 19 role agents + PROTOCOL/HANDOFFS/PR-REVIEW | being ported by another worker | done on merge |

## 2. Not copied (and why)

| IMS item | Why excluded |
|---|---|
| `PostingEngine`, `IPostingStrategy`, costing, availability, `StockBalance`/ledger, reversal | Stock-domain mechanics; HRNexus has no inventory |
| `DocumentActionPolicy` / `ApprovalPolicy` / approval tiers for stock documents, purchase-order state machine | Warehouse documents. The *pattern* (server-sent `allowedActions`, pure policies) IS adopted for leave/payroll/recruitment workflows |
| Per-(warehouse, product) locks and the global lock order | Stock concurrency. Adopt the *discipline* (one documented lock order, `ITransactionLocker`) for payroll runs |
| `[VisibleWith]` cost/valuation shaping, `IReportWriter` | Cost visibility is stock-specific. Adopt the same mechanism later for **salary/PII visibility** (new tag, same `ResponseShaper`) |
| Append-only ledger + DB triggers | Adopt for the audit log only |
| `warehouse scope` claims (`allwh`, `wh`) | Adapt to **location scope** (`StaffLocation`), not copy |
| SQL Server specifics: `rowversion`, `READ_COMMITTED_SNAPSHOT`, `sp_getapplock`, clustered-key rationale for int+PublicId | HRNexus is PostgreSQL 17: use `xmin` token, `pg_advisory_xact_lock`, heap tables. Intent kept (opaque ids, no counting leaks) |
| Excel import/export stack (ClosedXML) | Adopt only when HR needs it (payroll export in Phase 8) |

> **Moved to ADOPT:** the modules gate (`ModuleCatalog`, `RequiresModule`, `ModuleGateMiddleware`). The earlier exclusion ("HRNexus is one product") was wrong: the owner wants feature management back so each HRM phase (8-12) ships behind its own switch. See `phase-07h-feature-management.md`.

## 3. Strategy: incremental vs big-bang

Hypothesis: build HRM phases 8-12 in IMS style from day one, migrate legacy per feature behind tests. **Confirmed**, with two conditions.

- **Why not big-bang:** the app is live in production, 1,100+ tests encode behaviour, and the dispatcher/repository/GUID/cursor changes touch every handler and every FE service. A rewrite freezes delivery for weeks and risks the PII-encryption and auth behaviour that is hard to re-verify.
- **Risk of the hybrid:** two styles coexist (MediatR + `IAppDbContext` vs dispatcher + specifications). CLAUDE.md Rule 7 forbids blending. **Mitigation = IMS's own shrinking-allowlist pattern** (`NoRoleDecisionTests`): architecture tests list the legacy folders that may still use the old style; the list only shrinks, an entry for a migrated folder fails the build.
- **Condition 1:** cheap conventions first (section 4, 7b-7e) so new modules inherit hooks, architecture tests, ProblemDetails and OpenAPI rules on day one.
- **Condition 2:** the dispatcher swap must land **before** the HRM modules multiply handlers, otherwise phases 8-12 add ~50 more MediatR handlers to migrate. Mitigate by introducing the `IDispatcher` + `ICommand/IQuery` abstractions early (7f) as a thin adapter over MediatR, so new slices are written against IMS types and the old handlers are swapped mechanically later.
- **Main trade-off:** about 6-8 days of alignment work before Phase 8 starts, versus roughly double that migration cost later. MediatR 12 stays MIT, but 13+ is commercial, which is another reason to own the dispatcher.

## 4. Phased plan (alignment track; module phases keep numbers 8-12)

Order = cheapest and highest-leverage first; big rewrites last. Each phase follows PHASE_TEMPLATE.md.

| Phase | Deliverables | Validation | Size |
|---|---|---|---|
| **7b** Repo guardrails | `.editorconfig` C# rules + `src/Directory.Build.props` merge (keep nullable-as-errors); `.githooks/*` + `scripts/guard/*.mjs` + node tests; PR template, CODEOWNERS, `pr-guard.yml`, dependabot; `docs/architecture/` + `docs/TEMPLATE.md` + ADR 0001 "adopt IMS standard"; `TASKS.md` | hooks block a bad commit message and a direct push; CI green; `dotnet format --verify-no-changes` clean | S |
| **7c** Architecture tests + DI validation | New `tests/Architecture.Tests`: layer dependency rules, no `.Result`/`.Wait()`, no role-name decisions (shrinking allowlist), one public type per file for new folders (allowlist for legacy); `ValidateScopes`/`ValidateOnBuild`; options validated at start-up; `AddApplication()`/`AddPresentation()` | tests fail on a seeded violation, pass on main; app boots | S-M |
| **7d** Error contract | `ApiExceptionHandler` → ProblemDetails; `ProblemCodes`; typed domain exceptions (Conflict, Forbidden, NotFound, Concurrency) replacing `InvalidOperationException`→409 guesses; FE `ApiError` mapping in `error.interceptor`; update toasts + E2E | every exception type has a status/code test; E2E negative suite green; FE specs updated | M |
| **7e** API contract | `WireJson` (enums as strings, UTC `Z`); `Tag_action` operation ids; enum/nullable schema transformers; OpenAPI contract tests (ids, enums, operationIds pinned); evaluate `Microsoft.AspNetCore.OpenApi` over Swashbuckle | contract tests green; `openapi/v1.json` committed snapshot | M |
| **7f** Dispatcher abstraction | `IDispatcher`, `ICommand`/`IQuery`/handler interfaces, pipeline behaviours; adapter so new slices use IMS types while legacy stays on MediatR; slice template (`Features/<F>/UseCases|Contracts|Validators|Specifications`) documented | one pilot use case (small Phase 8 query) passes through both pipelines | M |
| **7g** FE workspace | Nx conversion (3 projects keep working), `@hrnexus/*` aliases, scope tags + `enforce-module-boundaries` (warn → error), `QueryLoader`/`ApiCommand` + `createPageState` in shared utils; Stylelint + Prettier hooks; i18n completeness test | `nx lint/test/build` green for all projects; boundary rule fails on a seeded violation | L (split in two) |
| **7h** Feature management | `SystemSettings` table + seeded `Module.*` rows (existing modules stay ON); `ModuleCatalog` (`cash-advances`, `payroll-locks`; later phases add their own), `[RequiresModule]` + `ModuleGateMiddleware` (404), `GET/PUT /api/modules` (`modules.manage`, reason, audit), permission hiding, FE `ModulesStore` + `requireModule` + admin Features page, SignalR refresh; needs 7c + 7d, best after 7f (`ListModules` can be its pilot slice), can run in parallel with 7g | gate-order integration tests (401/404/403/200), architecture test for gated endpoints, deploy-safety test, E2E toggle round-trip | M (~2 days) |
| **8** Payroll rebuild | First feature built fully IMS-style: vertical slices, specifications, `If-Match` concurrency token on payroll runs, ProblemCodes, `Domain.Tests` project for pure pay/deduction policies, FE feature lib + container lib per tier | per phase doc | per doc |
| **9-12** HRM modules | Same checklist; salary/PII visibility tag (`[VisibleWith]` pattern) in Phase 8-9; cursor pagination for audit/notifications when touched | review gate (7 perspectives + architecture tests) | per doc |
| **12b** Generated client | NSwag client from committed OpenAPI; replace hand-written services feature by feature; `withIfMatch` | contract drift test; services deleted per feature | L |
| **12c** Persistence style | `IRepository`/`IReadRepository`, specifications, `IUnitOfWork.ExecuteAsync` (retry on serialization/deadlock, audit in same transaction); migrate legacy areas when next touched (Leave first: audit found concurrency + overlap defects) | per-area tests unchanged and green; allowlist shrinks | L total, per area S-M |
| **12d** Dispatcher swap | Replace MediatR registrations with the dispatcher, bulk-mechanical (`bulk-editor` agent), remove package | all tests green; no `MediatR` reference | M |
| **12e** Cursor pagination + opaque cursors | `ICursorProtector`, seek lists for audit, notifications, leave history | cursor tamper tests | M |
| **12f** Public-id policy | Confirm Q1; document "Guid Id is the public id", add the OpenAPI "every id is a uuid" test, 404 for out-of-scope ids | test green | S |
| **12g** UI kit | Per Q2: `app-*` facades over PrimeNG in `shared/ui`, token rename, contrast script | contrast checks pass | M-XL |

Per-phase rule: a phase that changes the API wire format (7d, 7e, 12b) ships FE + E2E changes in the same PR; DB migrations follow the CLAUDE.md maintenance-window rule.

## 5. Open questions (ranked)

1. **Ids:** keep HRNexus's existing random `Guid` primary keys as the public ids (no int-key + `PublicId` hybrid)? On PostgreSQL the hybrid's clustered-index rationale does not apply and the migration is XL. Recommended: yes, keep.
2. **UI:** IMS builds its own `app-*` kit and bans component libraries; HRNexus uses PrimeNG 21. Replace it (XL) or wrap PrimeNG behind `shared/ui` facades? Recommended: wrap.
3. **Pause for alignment:** run 7b-7g (about 6-8 days) before Phase 8, or interleave (7b-7e now, 7f-7g in parallel with Phase 8 design)? Recommended: 7b-7f first, 7g in parallel.
4. **Test stack / Angular version:** IMS uses Jest + Spectator on Angular 22; HRNexus uses Vitest on Angular 21. Keep Vitest and stay on 21 until the standard is migrated?
5. **Database:** confirm HRNexus stays on PostgreSQL 17 (IMS is SQL Server), with `xmin` and advisory locks as the equivalents.
