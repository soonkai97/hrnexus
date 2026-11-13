# Git workflow, CI/CD and guards

The repository is public and `master` is the only long-lived branch. Everything reaches it through a pull request.
Modelled on the IMS standard (`github.com/soonkai97/IMS`, `docs/team/GIT-WORKFLOW.md`).

**One-time setup per clone (30 seconds):**

```bash
npm run hooks:install      # sets core.hooksPath=.githooks, pull.ff=only, push.autoSetupRemote, merge.ff=false
```

## The flow

1. Pick the phase step (`docs/process/phase-NN-*.md`). `git switch -c feat/<short-name>` (types: `feat`, `feature`, `fix`, `chore`, `docs`, `refactor`, `test`, `ci`, `perf`, `hotfix`, `release`).
2. Commit small, with Conventional Commits: `feat(leave): count working hours only`. Subject at most 72 characters, no trailing full stop, blank line before the body.
3. `git push` (the first push sets the upstream), open a PR into `master` with the template. The PR title becomes the commit on `master`, so it follows the same format.
4. The required checks (below) must be green and every review conversation resolved. Non-trivial PRs also get the independent review in `docs/team/PR-REVIEW.md` section 8.
5. **Squash-merge** (the only allowed method). The branch is deleted on merge. Then `git switch master && git pull`.
6. Rebuild Docker locally (`docker compose build --no-cache`) and hard-refresh the browser. There is no deploy step while there is no production.

Never push to `master`, never force-push, never commit `.env` or keys. The ruleset and the hooks both enforce this.

## What is enforced, and where

| Rule | Local hook | CI | GitHub ruleset "master protection" |
|---|---|---|---|
| Conventional commit subject (max 72) | `commit-msg` | PR title and every commit (`Git workflow guard`) | squash-only merge |
| Branch name `<type>/<name>` | `pre-commit` | `Git workflow guard` | |
| No commit or push straight to `master` | `pre-commit`, `pre-push` | | PR required |
| No force-push, no deleting `master` | `pre-push` | | yes |
| Linear history | `merge.ff=false`, `pull.ff=only` | | yes |
| No secrets, keys, backups, build output, files over 2 MB | `pre-commit` | `Git workflow guard` | |
| ESLint on staged Angular files | `pre-commit` | `Web (lint, test, build)` | required check |
| Backend unit tests before push (only when `backend/` changed) | `pre-push` | `API (build, unit, integration)` | required check |
| PR size at most 1500 changed lines (lock files and EF designer/snapshot files excluded) | | `Git workflow guard` | |
| Review conversations resolved | | | yes |

The rules are written once in `scripts/guard/lib.mjs`; hooks and CI import the same file. Rule tests: `npm run guard:test`.

**AI co-author trailers:** the IMS standard rejects them; HRNexus commits already carry them, so the check is off. Enforce it by setting `GUARD_REJECT_AI_COAUTHOR=1` (local) or adding it to the `Run guard` step env in `pr-guard.yml`.

**Overrides (emergencies only; CI and the ruleset still apply):** `ALLOW_MAIN_COMMIT=1`, `ALLOW_PUSH_MAIN=1`, `SKIP_PREPUSH_TESTS=1`. A test value that looks like a secret: add `guard:allow-secret` on that line. A PR that cannot be split (for example removing a whole feature) needs the owner to add the `large-change` label.

## Required checks (exact names, add them to the ruleset)

Status check contexts are the **job names**:

| Check name | Workflow | What it proves |
|---|---|---|
| `API (build, unit, integration)` | `ci.yml` | restore, production build with warnings as errors, Application + Infrastructure unit tests, Integration tests on real PostgreSQL (Testcontainers) |
| `Web (lint, test, build)` | `ci.yml` | ESLint, production builds of shared-lib, admin-app and staff-app, Vitest for all three |
| `Git workflow guard` | `pr-guard.yml` | PR title, commits, branch name, secrets and forbidden files, PR size |

Not required (run and report only):

| Check | Why not required |
|---|---|
| `Dependency audit` (`ci.yml`) | The repo has known high-severity NuGet and npm advisories today, so it only warns. Fix them (Dependabot PRs), then set the repository variable `AUDIT_STRICT=true` and add it to the required checks. |
| `CodeQL (csharp)`, `CodeQL (javascript-typescript)` | Findings appear under Security -> Code scanning; make them required after the first clean run. |
| `E2E (Playwright)`, `DAST` | Slow; manual and weekly only, never on PRs. |

Add the checks to the existing ruleset (after the first PR run has made the names visible to GitHub). `PUT` replaces the whole ruleset, so send the full body (replace `<id>` with the ruleset id from `gh api repos/soonkai97/hrnexus/rulesets`):

```bash
gh api -X PUT repos/soonkai97/hrnexus/rulesets/<id> --input - <<'JSON'
{
  "name": "master protection", "target": "branch", "enforcement": "active",
  "conditions": { "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] } },
  "bypass_actors": [],
  "rules": [
    { "type": "deletion" }, { "type": "non_fast_forward" }, { "type": "required_linear_history" },
    { "type": "pull_request", "parameters": { "required_approving_review_count": 0, "dismiss_stale_reviews_on_push": true,
      "require_code_owner_review": false, "require_last_push_approval": false, "required_review_thread_resolution": true,
      "allowed_merge_methods": ["squash"] } },
    { "type": "required_status_checks", "parameters": { "strict_required_status_checks_policy": true,
      "required_status_checks": [
        { "context": "API (build, unit, integration)" },
        { "context": "Web (lint, test, build)" },
        { "context": "Git workflow guard" }
      ] } }
  ]
}
JSON
```

Repository-level auto-merge and delete-branch-on-merge are enabled; enable auto-merge on an individual PR only when you want that PR to merge itself once the checks pass.

## Lint gates

The Web job runs ESLint with zero warnings allowed (`npm run lint -- --max-warnings 0`, Playwright specs included) and a Prettier ratchet
on changed files (`npm run format:check`, script `scripts/format-check.mjs`): new files must be formatted, files that were already formatted
must stay formatted, and legacy unformatted files are skipped so nothing is mass-reformatted. Run `BASE_SHA=origin/master npm run format:check`
locally and fix with `npx prettier --write <file>`. The API job runs `dotnet format style` and `dotnet format analyzers` (whitespace layout is
not enforced yet). Stylelint for SCSS is planned after the Angular 22 upgrade merges, because it adds devDependencies that would conflict with it.

## No production deployment

There is currently no production server and no deploy workflow (the manual `deploy.yml` was removed together with the server on 13 Nov 2026). When a server exists, recreate a deploy workflow with the same rules: manual `workflow_dispatch` only, a protected GitHub environment with a required reviewer, deploy keys and host details stored as environment secrets (never in the repository), only the tip of `master` after green CI, and a fresh database backup plus maintenance window for any release with a destructive migration. `deploy.sh` (maintenance mode, `--wait` for healthy containers) and `scripts/backup-db.sh` / `restore-db.sh` stay for that day.

## Releases

Tag a commit that is on `master` and green in CI: `git tag v1.2.0 && git push origin v1.2.0` (`v1.2.0-rc.1` makes a pre-release). `release.yml` verifies both conditions and creates the GitHub Release with generated notes. It publishes no image and deploys nothing. (Tag pushes are not blocked by the branch ruleset; add a tag ruleset if you want tags restricted.)

## Also turn on (Settings -> Code security)

Secret scanning and push protection, Dependabot alerts and security updates. `dependabot.yml` already groups weekly NuGet and npm updates and monthly Actions and Docker updates.
