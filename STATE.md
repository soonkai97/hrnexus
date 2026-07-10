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
- `WorkLog` stores `RoleName` as an uppercase **snapshot string**, not an FK to `Roles`. Any
  per-job rule that must apply to an existing shift (e.g. the max-shift-days cap on clock-out /
  admin-close) has to resolve the job by a name-join back to `Roles`, since the log doesn't carry
  the job's GUID.
- `Role.MaxShiftDays` defaults to 30 for new/existing jobs; the configurable ceiling is 100
  (`AppConstants.WorkLog.MaxShiftDaysCeiling`), separate from the default
  (`AppConstants.WorkLog.MaxShiftDays`). Migration `AddRoleMaxShiftDays` backfilled all existing
  jobs to 30 (`NOT NULL DEFAULT 30`).
- Shift overlap is enforced **per staff member, across all jobs** — not per-job. A person cannot
  clock into a second job while one shift is open (`StartWorkLogCommand` refuses + a partial
  unique index backs it up), and clock-out/manual/admin-edit all reject any time-overlap with an
  existing shift regardless of which job it's for. Confirmed by reading
  `StartWorkLogCommand.cs`, `EndWorkLogCommand.cs`, `CreateManualWorkLogCommand.cs`,
  `UpdateWorkLogCommand.cs`, `AdminEndShiftCommand.cs` — all guard on `EmployeeId` only.
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

**Date:** 2026-07-09/10 (session covering multi-day shifts + per-job caps + roster hiding)

**Shipped (all merged to `master`, local Docker rebuilt and healthy):**
- PR #48 `8b8d536` — multi-day shifts (removed 24h clock-out/manual limit, unified 30-day cap)
- PR #49 `ef67d32` — per-job configurable max shift days (default 30)
- PR #50 `df58585` — hide HRADMIN/HRMANAGER from staff roster + unhide toggle
  (`Staff.IsHidden`, `GetStaffListQuery.IncludeHidden`)
- PR #51 `acca378` — raised per-job max-shift-days ceiling 30 → 100

**Master HEAD:** `acca378`. Working tree clean. Local Docker last rebuilt at build `20260709.02`.

**Not done / explicitly deferred:**
- Customer's "Shooting Game" job (fake-gun target practice, hourly RM150, EN/VI names proposed)
  — user said "leave it", not yet added to the system.

**Resume pointer:** No in-flight branch or uncommitted work. Next session starts clean from
`master`. If the user asks to continue, natural next steps are either adding the Shooting Game
job (translations already drafted above) or whatever they bring up next — nothing is
blocking or half-finished.
