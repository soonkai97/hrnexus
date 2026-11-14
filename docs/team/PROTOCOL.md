# Team protocol

The agents in `.claude/agents/` form one delivery chain. They cannot message each other; they collaborate through
files in this repo, and the **orchestrator** (the main Claude session) routes work between them.

## Chain (who feeds whom)

```
product-manager -> business-analyst -> ui-ux-designer -> fe-designer -> fe-architect -> fe-framework
                         |                                                  ^              |
                         v                                                  | contract     v
                 be-system-designer -> be-architect -> be-dba -> be-framework        qa-novice-staff / qa-power-manager / qa-edge-accessibility
                         |                                  |         |                    |
                         v                                  v         v                    v
                 data-flow-analyst              unit-test / integration-test       behavior-test
project-manager: reads HANDOFFS.md, keeps STATE.md and the phase docs true, names the next role.
security-engineer: independent; threat-models, pen-tests the local stack, reviews PRs for security.
```

## Source of truth

`CLAUDE.md` (process rules and patterns), `STATE.md`, and `docs/process/phase-*.md` (scope, steps, validation
criteria). Current scope: Phase 7 (remove clock-in/out), then the HRM roadmap: 8 payroll rebuild, 9 documents and
compliance, 10 onboarding/offboarding, 11 recruitment, 12 performance reviews.

## Lanes (who may edit what)

| Role | May edit |
|---|---|
| product-manager, business-analyst | docs/product, docs/ba |
| project-manager | docs/pm, STATE.md, status and sign-off sections of docs/process/phase-*.md |
| ui-ux-designer, fe-designer | docs/design, projects/*/src/styles (tokens only) |
| fe-architect | docs/architecture/frontend.md (review projects/) |
| fe-framework | projects/ |
| be-architect | docs/architecture/backend.md (review backend/src) |
| be-system-designer, data-flow-analyst | docs/design, docs/dataflow |
| be-dba | backend/src/Infrastructure/Persistence (configurations, migrations) |
| be-framework | backend/src |
| unit-test-engineer | backend/tests/Application.Tests, Angular `*.spec.ts` |
| integration-test-engineer | backend/tests/Integration.Tests |
| behavior-test-engineer | tests/features, tests/e2e |
| security-engineer | docs/security, tests/security (reports and proof-of-concept scripts; never backend/src or projects/) |
| qa-* personas | docs/qa only |

Defects found outside your lane go to the owner via HANDOFFS.md; do not fix them yourself.

## Handoff log

Append one entry per finished piece of work to `docs/team/HANDOFFS.md` (newest at the bottom):

```
### <date> <from-role> -> <to-role(s)>
- Produced: <paths>
- Needs from <role>: <ask>
- Decisions others must respect: <list or none>
- Open conflicts: <list or none>
```

## Definition of done (any role)
1. Output is in a file, not only in chat.
2. Anything buildable was built (`dotnet build`, `npx ng build web`) and the tests for what changed were run; the result is stated plainly.
3. A HANDOFFS.md entry exists naming the next role.
4. CLAUDE.md Process rules were followed (surgical changes, TDD, feature branch + PR, no temp files on drive C:).

## Test runs
Run only the tests for the code you changed, plus the build. Before a PR is opened, the author runs the full Rule 18 suite
(`npx ng test shared-lib|web --no-watch`, `dotnet test`). `backend/tests/Integration.Tests` needs Docker
(Testcontainers). Never merge on a red check; a failure is fixed by the author and re-checked, not waved through.

## Orchestrator rules
- Run roles in parallel only when their lanes and inputs are independent.
- Read HANDOFFS.md after every agent finishes and dispatch the named next role.
- Escalate to the user for anything destructive or hard to reverse (dropping tables or columns, migrations on live data,
  force push, deploys, merges) and for scope decisions the phase doc leaves open.
- `master` only advances through merged PRs; agents never push to `master`.
