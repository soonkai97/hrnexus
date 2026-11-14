# Pull request review

Every role in `.claude/agents/` can review a pull request or a branch from its own point of view.
Reviewers are **read-only**: they report, they do not fix. The orchestrator (main session) picks the reviewers,
collects the reports and tells the author what to change.

## 1. Pick the reviewers

Map the changed paths to roles. Always include `be-architect` or `fe-architect` for code, and `qa-*` personas only when the UI changed.

| Changed path | Roles |
|---|---|
| `backend/src/Domain`, `backend/src/Application` | be-architect, be-system-designer (state changes, concurrency), unit-test-engineer |
| `backend/src/Infrastructure/Persistence`, migrations | be-dba, be-architect, security-engineer (PII columns) |
| `backend/src/Api`, auth, cookies, nginx, Docker, `.env` handling | security-engineer, be-architect |
| `backend/tests` | unit-test-engineer, integration-test-engineer |
| `projects/*` (components, stores, services) | fe-architect, fe-framework, unit-test-engineer |
| `projects/*/src/styles`, `*.scss` | fe-designer, fe-architect |
| `tests/e2e`, `tests/features` | behavior-test-engineer |
| `docs/process/phase-*.md`, `STATE.md`, `CLAUDE.md` | project-manager, product-manager |
| any user-visible flow | business-analyst, ui-ux-designer, qa-* personas |
| payroll, leave balances, PII, permissions | data-flow-analyst plus security-engineer |

## 2. What a reviewer receives

- The PR number or branch name, the base branch (`master`), and the PR description.
- The diff: `git diff <base>...<head>` (or `gh pr diff <n>`).
- A reviewer reads the *whole* changed files, not only the hunks, and follows references into other files when needed.

## 3. How to review (all roles)

1. Stay in your lane. Report what you are qualified to judge. A concern in another lane goes under "Cross-lane notes" naming the role.
2. Verify, do not guess. If a claim depends on behaviour, build or run it (`dotnet build`, `dotnet test`, `npx ng test <project> --no-watch`, `npx ng build <project>`, the Docker stack, the UI). Say what you ran and the result.
3. Check the change against its source of truth: the phase doc step and validation criteria, the CLAUDE.md rule, the acceptance criteria.
4. Prefer few, concrete findings over many vague ones. Every finding names `file:line`, what is wrong, the failing case, and a suggested fix.
5. Do not restyle. Skip taste. Match the codebase's conventions (CLAUDE.md Rule 11).
6. Never post to GitHub (comments, reviews, approvals) unless the user asked for it in this session.
7. Do not create temp or scratch files on drive C:.

## 4. Severity and verdict

| Severity | Meaning | Blocks merge |
|---|---|---|
| BLOCKER | Wrong behaviour, data loss or corruption, security hole, broken build/test, a CLAUDE.md rule or phase-doc criterion violated | yes |
| MAJOR | Likely bug, missing test for a rule, layering or contract violation, performance risk | yes unless the author explains |
| MINOR | Maintainability, naming, small gaps | no |
| NIT | Optional polish | no |

Verdict: **APPROVE** (no BLOCKER/MAJOR), **REQUEST CHANGES** (any BLOCKER/MAJOR), or **COMMENT** (questions only).

## 5. Report format

Write to `docs/reviews/<pr-or-branch>/<role>.md` and return the same content (under 300 words) to the orchestrator:

```
# Review: <pr/branch> by <role>
Verdict: APPROVE | REQUEST CHANGES | COMMENT
Ran: <commands and results, or "read only">

| # | Sev | Where | Finding | Suggested fix |
|---|-----|-------|---------|---------------|

Cross-lane notes: <role>: <concern>
Questions: ...
```

Then append one entry to `docs/team/HANDOFFS.md` (see `PROTOCOL.md`).

## 6. Checklists by role

**be-architect** - Dependencies point inward (Domain has none, Application only Domain, Api uses Infrastructure only for DI). Handlers depend on interfaces. No logic in controllers. Commands keep inline guards (handler-direct tests bypass the MediatR pipeline); new boundary validation goes in a FluentValidation `AbstractValidator`. Audit rows are written inside the same transaction (`audit.LogAsync`, `performedById: null` for the caller). Exceptions map to the existing status codes (`ArgumentException` to 400, not-found, conflict). New permission keys are enforced on the endpoint. New config keys validated at start-up. No blocking async calls.

**be-system-designer** - Transactions and retries around state changes. Optimistic concurrency (`xmin`) and conflict handling. Idempotency and double-submit behaviour (see the `UpsertLeaveCommand` guard). Bulk commands: one `SaveChangesAsync`, whole-selection guards (e.g. last admin). Failure modes named, each with a test.

**be-dba** - Migration is additive unless the phase doc approves a destructive change; destructive migrations need a fresh backup and a maintenance window and were confirmed by the user. Indexes match the new queries, decimal precision, `xmin` where concurrency matters, partial unique indexes where needed, no cascade that destroys history, PII columns stay `text` with the `enc1:` ciphertext. Seed idempotent. Query plan or timing for any new hot query.

**unit-test-engineer** - Every new domain rule or branch has a deterministic test; tests encode intent (Rule 9), not implementation; no sleeps, real clocks or randomness; a failing-case test per guard; the CLAUDE.md test patterns are followed (InMemory seeding via navigation collections, stub routes, no `detectChanges()`).

**integration-test-engineer** - New endpoints have status-code, permission and validation tests on real PostgreSQL (Testcontainers); concurrency-sensitive paths have a parallel test.

**behavior-test-engineer** - Phase validation criteria are covered by named scenarios; the golden path plus one edge case per feature and the auth flow are covered (Rule 16); permission-gated pages are tested with and without the permission.

**data-flow-analyst** - Trace each new or changed field UI to API to entity to report/export; flag places where values can diverge, double count, or expose PII in logs, audit or exports.

**business-analyst** - The change satisfies the phase doc and its acceptance criteria, edge cases handled, no silent scope change, HR terms used consistently.

**product-manager** - Scope matches the phase; nothing from a later phase sneaks in; user-visible copy and behaviour match the intent.

**project-manager** - PR links a phase step, description says what/why/how tested, STATE.md and the phase doc status are updated, size is reviewable (split if large), risks and follow-ups recorded.

**ui-ux-designer** - Flow works at phone width (self-service pages) and at desktop width (management pages) in the one web app; empty, loading (skeleton) and error states; English copy; confirmation only where it protects data.

**fe-designer** - Uses design tokens (`$neon-*`, `$bg-*`, CSS custom properties), no hard-coded colours or spacing, contrast AA, 44px targets, dark and light mode, responsive table stack pattern where tables exist.

**fe-architect** - Standalone components, signals, no new `BehaviorSubject`/`Subject`, state in stores or signals, lazy routes, guards (`authGuard`, `permissionGuard`) on every new route, `ngModel` form pattern unless reactive is justified, PrimeNG overlays use `appendTo="body"`, `:host :deep()` instead of `.class :deep()`, no full-page spinner, no `environment.ts`.

**fe-framework** - `npx ng build` and unit tests pass for the touched projects; no direct edits to backend; shared code lives in shared-lib; spec file exists for every new component/service/store/guard (Rule 17 scan returns 0 gaps).

**security-engineer** - Every new endpoint has an authorization policy; ids from the client are never trusted (BOLA); request DTOs cannot over-post (roles, IsActive); inputs are length- and range-bounded; no secrets, keys or connection strings in code or config; PII stays encrypted at rest and out of logs; errors expose no internals; file uploads validate type by content, size, name and path; no new dependency with known vulnerabilities; nginx security headers/CSP not weakened or duplicated at the API; auth cookies stay httpOnly/SameSite=Strict; rate limiter and lockout intact.

**qa-novice-staff / qa-power-manager / qa-edge-accessibility** - Run the PR's UI (built branch or rebuilt Docker stack) as that persona and report defects with steps, expected and actual. The accessibility persona checks keyboard, labels, contrast, 360px and 1440px, wrong-role access.

## 7. GitHub side

Work lands through a feature branch, a PR and a squash-merge into `master` (CLAUDE.md Rule 18). Agent roles are not GitHub users,
so their reviews are posted by the orchestrator on request (`gh pr review`), never automatically.

## 8. Review gate before merge

Passing tests is necessary, not sufficient: agents author most changes and their own tests share their blind spots. Before the orchestrator
merges any non-trivial pull request an independent reviewer reads it:

- **Applies to:** every PR over about 200 changed lines, and every PR, whatever the size, that touches payroll or any money figure, permissions,
  authentication, cookies or security headers, a database migration, PII encryption, the audit trail, or Docker/nginx/deploy scripts.
  Pure documentation and one-line fixes are exempt.
- **Who:** an agent that did not write the change and has not seen the author's reasoning (`deep-reviewer` or the matching role from section 6).
  It gets the diff and the repository, not a summary of intent.
- **What it checks:** logic errors and missed edge cases, payroll and rounding correctness, authorization and ownership checks, injection and logging of user input,
  data loss on partial input, concurrency and idempotency, migrations (Up, Down, existing data), and whether the tests would actually fail if the change were wrong.
- **Lean check (every PR):** "Which phase step needs this? Is there a simpler way? Does it add a dependency or a configuration surface?" (CLAUDE.md Rule 2). Flag over-engineering.
- **Output:** findings ranked Blocker / Major / Minor with file, line and a concrete failing scenario.
- **Rule:** a Blocker or Major is fixed (or explicitly accepted by the user) before merge; Minor findings become follow-up items in the phase doc.
