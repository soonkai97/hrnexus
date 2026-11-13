# STATE.md

Session-continuity file. Read this at the start of a session before new investigation;
update it before ending a session that produced new verified facts, diagnosed failures,
or distilled rules. Structure: Verified facts → General rules → Open failures → Lessons
learned → Last session (resume pointer).

## Verified facts

- `HRADMIN` is real seed data (`SeedData.cs`) — the system's default admin login. `HRMANAGER`
  is **not** seeded anywhere in code; it's a real manager account created later. Both are now
  hidden from the roster via `Staff.IsHidden`, but neither should ever be treated as safe to
  delete outright.
- **Clock-in/out (WorkLog), per-job `MaxShiftDays` and shift-derived pay were removed in Phase 7**
  (`docs/process/phase-07-remove-shift-log.md`). Earlier facts about shift overlap, the 30/100-day caps
  and `WorkLog.RoleName` snapshots are obsolete. `Role.HourlyRate`/`PayType` remain until Phase 8.
- The two leave background jobs (Jan-1 entitlement reset, daily auto-reject of overdue leave) live in
  `Infrastructure/Jobs/LeaveJobs.cs`. They once shared a file with the long-shift reminder: never delete
  that file wholesale.
- The user's personal code standard is the IMS repo (github.com/soonkai97/IMS); alignment plan in
  `docs/process/architecture-alignment-ims.md`. A 2026-11-13 leave audit found defects (clock-time
  hours, cancelled leave blocks re-apply, carry-over overwritten, self-approval, cross-year leave);
  not yet fixed.
- In this dev environment, the `Bash` tool has intermittently failed (`exit 1`, no output) on
  plain `git status`/`branch` calls from the repo root, while the same commands work fine via
  the `PowerShell` tool. Root cause not diagnosed — treat as a known flake, not a real repo issue.
  **Prefer PowerShell for git commands here.**
- `gh pr create --body <heredoc>` fails under PowerShell (no heredoc support) — write the PR body
  to a scratchpad `.md` file and pass `--body-file` instead.

## General rules

- When adding a business-rule cap (e.g. max shift days), model the **default** and the
  **configurable ceiling** as two distinct constants, not one. Lets the ceiling move later
  (30 → 100) without touching what new records default to.
- Additive migrations (`AddColumn ... NOT NULL DEFAULT x`) are safe to ship without a maintenance
  window. Reserve maintenance-window treatment for destructive/type-changing migrations (e.g. the
  earlier DOB `timestamptz → text` PII migration).
- After any shared-lib model/service edit, run `npx ng build shared-lib` **before** testing or
  building the consuming apps (admin-app/staff-app) — otherwise they compile against a stale
  `dist/shared-lib` and produce misleading type errors.
- For an ambiguous or potentially sensitive term (slang, unclear customer request), ask what it
  actually refers to in real-world terms before implementing/translating it — don't assume the
  worst case, but don't build it on a literal translation either. (Case: "1炮1小时" turned out to
  be a fake-gun shooting-game attraction, not what the characters could otherwise imply.)
- Roles/Jobs are runtime **data**, not schema — adding one doesn't need a migration, PR, or
  deploy. It's a POST from the Admin Portal's Jobs page (or `CreateRoleCommand` directly).

## Open failures

- (none currently open)

## Lessons learned

- Config ceilings and defaults are different concerns from day one — don't conflate "what a new
  record starts at" with "what a user is allowed to raise it to." Splitting them avoids a rework
  when the customer later asks to raise just the ceiling (as happened: 30 → 100 was a clean
  one-constant change because the split already existed).
- When a customer request could describe something illegal or against policy, get the real-world
  context directly rather than either refusing outright or complying literally — the honest ask
  ("what does the staff member actually do?") resolved this session's ambiguous case cleanly.

## Last session

**Date:** 2026-11-13 (Phase 7 landed; repo hardening; planning)

**Done (merged to `master` through PRs):** Phase 7 removed clock-in/out (`DropWorkLogs` migration created, not deployed
anywhere); IMS-style CI/CD, hooks and a protected `master` (PR-only, squash, required checks API, Web, Git workflow
guard); dependency security upgrades (Dependabot alerts 144 to 1); `.gitignore` hardening; git history purged of two
SQLite files, three demo seed files and an employer email; role agents under `.claude/agents/`.

**State:** there is **no production deployment** (the server was removed on 13 Nov 2026) and no real data, so no
backup or maintenance window is needed until a new deployment exists. Public docs no longer describe the old server.

**Planned (docs only):** `docs/process/phase-07j-remove-locations.md`, `phase-07i-english-only.md`,
`architecture-alignment-ims.md` (7b to 7g), `phase-07h-feature-management.md`, then HRM phases 8 to 12. The PRD lives
as a private Claude Doc (HRNexus PRD v1.0).

**Resume pointer:** next the Locations removal (7j), then English only (7i); the first admin password and all keys must
come from the environment before any deployment (the seed currently hardcodes a development password).
