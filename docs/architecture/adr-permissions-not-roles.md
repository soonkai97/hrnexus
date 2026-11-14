# ADR: Access is decided by permissions, never by a job's name

> **Status:** Accepted
> **Date:** 2026-11-14
> **Phase:** 7o, `docs/process/phase-07o-retire-role-names.md`

---

## Context

- Every endpoint and page was already gated by a permission (`perm:<key>` policies, `permissionGuard`), resolved from job defaults and per-user overrides. The two seeded job names, `SUPERADMIN` and `MANAGER`, survived as leftovers: a token claim nothing read, a frontend tier (`admin | manager | employee`) used by no route, dead controller branches, and the recipient lists for notifications.
- The recipient lists caused a real defect: pending-leave, withdrawn-leave and password-reset notices went to people **named** manager or administrator. A custom job holding `leave.approve` never heard about leave waiting for it, and a job merely named MANAGER was told about things it could not act on.
- The names also gave a false sense of protection. The only check that stopped an administrator account from being deleted compared a job name, while the real lock-out risk (the last person able to manage jobs and permissions) was guarded for deactivate but not for delete.

## Decision

No code compares a job's name to a constant. The names `SUPERADMIN` and `MANAGER` no longer exist.

- **Notifications follow permissions:** holders of `leave.approve` (pending and withdrawn leave) and `staff.manage` (password-reset requests), by the same rule as the permission resolver (job grants, plus per-user grants, minus per-user revokes, active accounts only). The person a notice is about is never told.
- **Lock-out protection is by permission:** the last active holder of `roles.manage` cannot be deactivated or deleted.
- **The first administrator job** is the one job flagged `IsSystem`, found by the flag. On a fresh install it is created as `ADMINISTRATOR`. It always holds every permission (granted at each start, and its permissions cannot be edited), cannot be deleted, and can be renamed.
- **The documented first-run account** (`HRADMIN`) is created only in an empty database, so deleting or renaming it can never bring the default password back.
- The access token carries no role claim; the frontend has no role tier.
- A migration renames `SUPERADMIN` to `ADMINISTRATOR` (or folds it into an existing `ADMINISTRATOR`, so nobody loses access) and makes `MANAGER` an ordinary job.

## Consequences

- A person's access is explained by one thing: the permissions their jobs and overrides grant. A new company can name its jobs anything.
- On an existing install, `MANAGER` is an ordinary job: it keeps its members and permissions but no longer receives every new catalog permission automatically; a new permission must be granted to it on the roles page. Only the first administrator job is granted new permissions automatically.
- Deleting the seeded administrator is possible once another active person holds `roles.manage`; deleting the last one is refused.
- Notices can reach more people than before (everyone holding the key) and fewer (nobody named MANAGER without the key).

## How to reverse

1. Run the migration's `Down` **before** reverting the code (the old seed would otherwise create a second `SUPERADMIN`).
2. Revert the sub-phase PRs in reverse order.
