# Phase [N]: [Title]

> **Status:** `Draft` | `In Progress` | `Complete` | `Reviewed`
> **Created:** YYYY-MM-DD | **Updated:** YYYY-MM-DD
> **Depends on:** Phase [N-1] (or "None")

---

## Objectives

What this phase achieves — 3–5 measurable outcomes:

1.
2.
3.

---

## Scope

### In scope
- Item that will be done

### Out of scope (deferred)
- Item explicitly excluded — note which future phase picks it up

---

## Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| Description | Infra / Code / API / External | Done / Pending |

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Description | Low/Med/High | Low/Med/High | Action to reduce risk |

---

## Implementation Steps

Each step should be small enough to complete and review in one session.
Steps marked `[TDD]` require tests written **before** implementation.

1. `[TDD]` Step description — estimated size (S/M/L)
2. Step description
3. ...

---

## Validation Criteria

All must pass before this phase is marked Complete:

- [ ] Criterion (automated test / manual check / metric)
- [ ] All new code has corresponding tests
- [ ] Coverage threshold met (≥70% for changed files)
- [ ] No TypeScript errors (`ng build` clean)
- [ ] Multi-perspective review completed (see `REVIEW_CHECKLIST.md`)

---

## Rollback

If this phase causes regressions:

- **Git:** revert commits on feature branch; do not merge to main
- **Specific:** [describe what to undo if something specific breaks]

---

## Review Notes

_Filled in after phase completes. Record findings from multi-perspective review here._

### Issues found
| Issue | Perspective | Severity | Affects Phase | Resolution |
|-------|-------------|----------|---------------|-----------|

### Plan updates
_How did this phase's findings change the plan for Phase [N+1]?_

---

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives from `REVIEW_CHECKLIST.md`)
- [ ] Phase [N+1] plan updated based on findings
- [ ] CLAUDE.md updated if any new patterns were established
