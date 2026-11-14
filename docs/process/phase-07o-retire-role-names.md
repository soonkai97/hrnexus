# Phase 7o: Retire the SUPERADMIN and MANAGER role names (access is decided by permissions only)

> **Status:** `Draft` (plan reviewed by the opus reviewer on 14 Nov 2026; findings folded in below)
> **Created:** 2026-11-14 | **Updated:** 2026-11-14
> **Depends on:** Phase 7n merged (one app, `portal.admin` retired). Independent of 7m-b and Phase 8, but **comes before Phase 8c** so payroll never inherits a role-name rule.
> **Why:** the owner asked (14 Nov 2026) to remove SUPERADMIN entirely. Permissions already decide every endpoint and every page, so the two seeded role names are now leftovers, and they cause one real defect: the notifications that tell someone about a pending leave request, a withdrawn leave or a password-reset request go to people **named** MANAGER or SUPERADMIN, not to people who hold `leave.approve` or `staff.manage`. A custom role with `leave.approve` never hears about pending leave.

---

## Objectives

1. **No code branches on a role name.** A role is a name plus a set of permissions; nothing compares `Role.Name` to a constant.
2. **Notifications go to the people who can act on them:** holders of `leave.approve` (pending and withdrawn leave) and `staff.manage` (password-reset requests), resolved from role permissions and per-user overrides, active accounts only, never the person the notice is about.
3. The **tier claim** in the access token and the frontend `admin | manager | employee` tier (`userRole`, `hasRole`, `roleGuard`) are removed; nothing reads them.
4. A fresh install seeds **one** bootstrap role, `ADMINISTRATOR`, holding every permission, and the `HRADMIN` account. The names `SUPERADMIN` and `MANAGER` no longer exist in code, seed, tests, docs or UI. Existing installs keep working through a migration.
5. **The protections that the names provided are replaced by protections on data and permissions**, so removing the names removes no safety: the bootstrap role (flag `IsSystem`) cannot be deleted and cannot lose permissions; the **last** holder of that role and the **last** active holder of `roles.manage` cannot be deleted or deactivated.

---

## Verified starting point (on `master`; checked by the reviewer against the code)

| Where                                                                                                                           | What it does today                                                                                                                                                                                                                         | Becomes                                                                                                                                       |
| ------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `UpsertLeaveCommand.NotifyManagersAsync`, `CancelLeaveCommand.AddManagerNotificationsAsync`                                     | notify active staff whose role is named MANAGER or SUPERADMIN (nobody is excluded on new and sick/emergency leave; the acting user is excluded on withdrawal)                                                                              | notify active holders of `leave.approve`, excluding the leave's owner and the acting user                                                     |
| `ForgotPasswordCommand`                                                                                                         | same, for password-reset requests (the bell links to `/staff`; nobody excluded)                                                                                                                                                            | notify active holders of `staff.manage` (the reset endpoint is gated on it), excluding the person asking                                      |
| `Staff.PermissionLevel`, `StaffConfiguration` `b.Ignore(e => e.PermissionLevel)`, the `ClaimTypes.Role` claim in `TokenService` | tier string; **nothing reads the claim** (no `IsInRole`, no `[Authorize(Roles=...)]`; `PermissionAuthorizationHandler` reads only `NameIdentifier`; `ShiftHub` uses plain `[Authorize]`)                                                   | removed together (the `Ignore` line too, or the build fails)                                                                                  |
| `StaffController.IsManagerCaller` (hard-coded `false`) and the five branches it guards                                          | "managers cannot edit admins" rule that is switched off                                                                                                                                                                                    | removed                                                                                                                                       |
| **`StaffController` delete (line 178): `IsAdminAsync`, used outside any `IsManagerCaller` branch**                              | **the only protection against deleting an administrator account** ("Admin accounts cannot be deleted"); `DeleteStaffCommand` has no check of its own                                                                                       | **replaced by permission-based protection (see Design); it must not simply be deleted**                                                       |
| `DeleteStaffCommand`                                                                                                            | does not call `RolesManageInvariant`, so someone with `staff.manage` but not `roles.manage` can delete the only active `roles.manage` holder (deactivate is protected, delete is not)                                                      | runs the same last-holder invariant as deactivate                                                                                             |
| `GetStaffListQuery.ExcludeAdmins`                                                                                               | always passed `false`                                                                                                                                                                                                                      | parameter removed (its tests with it)                                                                                                         |
| `SeedData`                                                                                                                      | creates roles MANAGER and SUPERADMIN (both `IsSystem`, every permission, re-added at every start) and creates HRADMIN **whenever the StaffId "HRADMIN" is missing**                                                                        | one bootstrap role found by the flag; HRADMIN created only when no active account holds it (see Design)                                       |
| `SetRolePermissionsCommand`                                                                                                     | never checks `IsSystem`: permissions removed from a system role are silently re-added at the next restart                                                                                                                                  | refuses to edit the bootstrap role, so "holds every permission" is true at edit time and the roles page shows it read-only                    |
| `RenameRole` / `DeleteRole` (and `roles.component.ts` lines 93 and 147)                                                         | system roles keep their name "because authorization matches on it" (no longer true)                                                                                                                                                        | the bootstrap role can be renamed (backend and roles page); it still cannot be deleted                                                        |
| Frontend `auth.service.ts` (`userRole`, `hasRole`), `roleGuard`, `UserRole`, `auth.model.ts` comment                            | tier derived from the names; `roleGuard` is used by no route                                                                                                                                                                               | removed with their specs                                                                                                                      |
| Tests, scripts and docs naming SUPERADMIN or MANAGER                                                                            | about 14 backend and 9 frontend spec files, 2 E2E files, `scripts/seed-and-demo-hardening.ps1`, CLAUDE.md, README, STATE, agent files, comments in `StaffRole.cs`, `StaffController.cs`, `staff.component.ts`, `staff-detail.component.ts` | reworded to "administrator" / "a role holding X"; the demo script is already broken (it sends role names where ids are needed) and is deleted |

---

## Design

- **`IPermissionResolver.GetHolderIdsAsync(string permissionKey)`** returns the ids of **active, not-deleted** staff who effectively hold a key: a role grant, plus a per-user grant, minus a per-user revoke (revoke wins). It starts from `db.Staff` (as `RolesManageInvariant.CountActiveAdminsAsync` does), so soft-deleted accounts never appear, and mirrors `PermissionResolver.ComputeAsync`. One query, not a loop; not cached (a fan-out is rare and must be current).
- **Notification recipients** are the holders minus the people the notice is about: pending leave excludes the leave's owner **and** `ActingUserId` (an admin recording leave for someone); withdrawal keeps its existing exclusion; a password-reset request excludes the person asking.
- **Delete protection moves from a name to permissions.** `DeleteStaffCommand` runs `RolesManageInvariant` (the last active `roles.manage` holder cannot be deleted, exactly as for deactivate) and a new rule: **the last active member of the bootstrap role cannot be deleted or deactivated**. The old "Admin accounts cannot be deleted" check is not carried over as a name rule; the two invariants cover the real risk. Tests for both.
- **Bootstrap seed (new and existing installs):**
  1. Find the bootstrap role: the role with `IsSystem = true` (if several, the oldest by `CreatedAt`, then `Name`); if none, create `ADMINISTRATOR` with `IsSystem = true`. If a custom role already named `ADMINISTRATOR` exists and no role is flagged, **flag that role** instead of inserting (so the unique-name index is never hit and startup never fails).
  2. Grant that role every catalog key it lacks (kept so a new permission never locks the first administrator out). Because `SetRolePermissions` now refuses to edit it, nothing is silently re-added.
  3. Create `HRADMIN` (with the documented first-run password) **only when no active account holds the bootstrap role**, never merely because the StaffId is missing. A deleted or renamed HRADMIN therefore cannot bring the default password back while another administrator exists.
- **Token:** `TokenService` stops writing `ClaimTypes.Role`; tokens already issued keep the extra claim until they expire (30 minutes) and nothing reads it. `AuthUser.roles` (the names shown to the person) stays.
- **Migration (existing installs), reversible and idempotent:**
  - `SUPERADMIN` becomes `ADMINISTRATOR` and keeps `IsSystem = true`. If an `ADMINISTRATOR` role already exists, flag it `IsSystem = true`, move the `SUPERADMIN` members and permissions onto it, and delete `SUPERADMIN`, so the name still disappears and no unique-index clash occurs.
  - `MANAGER` (when it exists) gets `IsSystem = false` and becomes an ordinary role that keeps its permissions and members.
  - `Down` selects the bootstrap role by its flag (a renamed role is still found), renames it back to `SUPERADMIN` only when `SUPERADMIN` is free, and sets `IsSystem = true` on `MANAGER` only when `MANAGER` exists.
- **Consequence for `MANAGER` on an existing install:** it no longer receives every new catalog permission automatically; a new permission must be granted to it on the roles page.
- **Frontend:** delete `userRole`, `hasRole`, `roleGuard`, `UserRole` and their specs; roles page: allow renaming the bootstrap role, show its permissions read-only with a one-line note; fix comments. Route protection is already `permissionGuard` only.

---

## Scope

### In scope

- Backend: resolver method, the three notification sites, delete protection, token claim, dead controller branches, `ExcludeAdmins`, seed, bootstrap-role edit rule, rename guard, migration, constants (`AppConstants.Roles` removed entirely).
- Frontend: tier code removal, roles page rename and read-only bootstrap role, comments.
- Tests (about 23 spec files, with the behavioural ones in 7o-a), E2E, the broken demo script, CLAUDE.md, README, STATE, agent files, ADR.

### Out of scope (deferred)

- Renaming the `/hubs/shift` hub; seeded-account hiding (`IsHidden`) stays as it is.
- A "who gets notified" setting per notification type (permission holders are the rule for now).
- Removing the `IsSystem` column: it keeps its meaning (the bootstrap role).
- Forcing a password change on the seeded account at first sign-in (worth its own small change; noted here because the default password is the risk the seed rule above reduces).

---

## Risks

| Risk                                                                                       | Likelihood | Impact | Mitigation                                                                                                                                                                                                                             |
| ------------------------------------------------------------------------------------------ | ---------- | ------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Deleting an administrator is no longer blocked and the default-password account comes back | Med        | High   | Delete protection by invariants (above); seed recreates HRADMIN only when no active holder of the bootstrap role exists; tests that delete HRADMIN while another administrator exists and while none does                              |
| Last `roles.manage` holder deleted by someone with only `staff.manage`                     | Med        | High   | `DeleteStaffCommand` runs `RolesManageInvariant`; test                                                                                                                                                                                 |
| Migration clash, ambiguous bootstrap role or a failed startup after a rename               | Low        | High   | Migration and seed handle: existing `ADMINISTRATOR`, several flagged roles, no flagged role; integration test runs the migration on a database holding `SUPERADMIN` and `MANAGER`, and again on one that already holds `ADMINISTRATOR` |
| Rollback fails after the code is reverted first                                            | Low        | Med    | Rollback order is stated (run `Down` **before** reverting the code); `Down` is idempotent and flag-based                                                                                                                               |
| Notifications go to the wrong people, to nobody, or to the person the notice is about      | Med        | Med    | Resolver tests (role grant, per-user grant, per-user revoke, inactive, soft-deleted, none); per-site tests for the exclusions; a custom role with the key is notified and a role merely **named** MANAGER or SUPERADMIN is not         |
| Existing `MANAGER` holders lose automatic new permissions                                  | Med        | Low    | Documented in the ADR and a note on the roles page                                                                                                                                                                                     |
| Removing the claim breaks an unseen consumer                                               | Low        | Low    | Reviewer confirmed nothing in `backend/src` reads it; grep scripts, workflows and tests again before removing; the E2E suite signs in as both account kinds                                                                            |

---

## Implementation steps

**7o-a: backend, with the tests that break (about 1.5 days)**

1. `[TDD]` `GetHolderIdsAsync`: tests first (role grant, per-user grant, per-user revoke wins, inactive and soft-deleted excluded, none), then `PermissionResolver`; the Application test stub gets the method.
2. `[TDD]` Re-point the three notification sites; a test per site for the recipients and the exclusions (owner, acting user, requester); update the existing notification tests so their recipients hold `leave.approve` or `staff.manage` instead of a role name.
3. `[TDD]` Delete protection: `DeleteStaffCommand` runs `RolesManageInvariant` and the last-bootstrap-member rule; same rule on deactivate; remove `IsManagerCaller`, `IsAdminAsync` and the dead branches.
4. `[TDD]` Bootstrap role: seed rewrite (flag lookup, existing-`ADMINISTRATOR` case, several flags, HRADMIN only when no active holder), `SetRolePermissions` refuses the bootstrap role, rename allowed, delete still refused.
5. Remove `Staff.PermissionLevel` (and the EF `Ignore`), the `ClaimTypes.Role` claim, `ExcludeAdmins` (and its tests), `AppConstants.Roles`; migration with `Down`; integration tests of both migration paths and of the notification fan-out.

**7o-b: frontend, E2E, docs (about 0.5 day)** 6. Remove `userRole`, `hasRole`, `roleGuard`, `UserRole` and their specs; roles page: rename the bootstrap role, read-only permissions with a note. 7. Reword the remaining spec files and the E2E specs; add an E2E scenario: a custom role holding `leave.approve` is notified when staff apply for leave, and an administrator cannot delete the last administrator. 8. Delete `scripts/seed-and-demo-hardening.ps1` (broken); CLAUDE.md (seeded credentials, role casing, notification text), README, STATE, agent files, ADR `docs/architecture/adr-permissions-not-roles.md`.

---

## Validation criteria

- [ ] `git grep -n -i "superadmin\|Roles.Admin\|Roles.Manager\|PermissionLevel\|roleGuard\|hasRole"` finds only history (phase documents) and the migration that renames the old role
- [ ] A custom role holding `leave.approve` receives pending-leave and withdrawn-leave notices; a role merely named MANAGER without the key does not; the owner and the acting user are never notified of their own request
- [ ] A custom role holding `staff.manage` receives password-reset requests; the requester does not
- [ ] The last administrator and the last active `roles.manage` holder cannot be deleted or deactivated; deleting HRADMIN while another administrator exists does not bring the default-password account back
- [ ] A fresh database seeds exactly one role (`ADMINISTRATOR`, every permission) and `HRADMIN`; an existing database with `SUPERADMIN` and `MANAGER` (and one that already holds `ADMINISTRATOR`) migrates and keeps every user's access; startup never fails on either
- [ ] The bootstrap role cannot be deleted or have permissions removed, can be renamed, and gets every new catalog permission; `MANAGER` on an existing install is an ordinary role
- [ ] `dotnet test` (all three projects), `npx ng test`, builds, ESLint, Prettier ratchet clean; E2E green against the rebuilt stack
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`), with the security and data perspectives done by the opus reviewer (it touches the token, the seed, delete protection and a migration)

---

## Rollback

- **Order:** run the migration's `Down` **first**, then revert the code. Reverting the code first lets the old seed create a new `SUPERADMIN` and the later rename then fails on the unique name index.
- **Git:** revert the sub-phase PRs in reverse order.
- **Data:** `Down` is flag-based and idempotent (see Design). Notification recipients are computed at send time, so nothing stored changes shape.

---

## Open questions

1. Should an existing install's `MANAGER` role be kept as an ordinary role (assumed, no one loses access), or merged into `ADMINISTRATOR`? Merging would change what those people can do.
2. Is `ADMINISTRATOR` the right name for the bootstrap role? It can be renamed on the roles page afterwards.
3. Should the seeded `HRADMIN` account be forced to change its documented first-run password at first sign-in? (Assumed: yes, as a separate small change, because it is the one credential that is public in the docs.)

---

## Review Notes

Plan review by the opus reviewer (14 Nov 2026), all CONFIRMED against the code:

| Issue                                                                                             | Perspective     | Severity | Affects Phase | Resolution                                                                       |
| ------------------------------------------------------------------------------------------------- | --------------- | -------- | ------------- | -------------------------------------------------------------------------------- |
| `IsAdminAsync` guards delete; removing it lets HRADMIN be deleted and re-seeded with `Admin@1234` | Security        | High     | 7o-a          | Delete protection by invariants; seed creates HRADMIN only with no active holder |
| `DeleteStaffCommand` skips `RolesManageInvariant`                                                 | Security        | High     | 7o-a          | Delete runs the invariant                                                        |
| `Down` and rollback order                                                                         | Data            | Med      | 7o-a          | Flag-based idempotent `Down`; rollback order stated                              |
| Seed edge cases (several flagged roles, custom `ADMINISTRATOR`)                                   | Data            | Med      | 7o-a          | Fixed lookup order; flag an existing `ADMINISTRATOR`                             |
| Bootstrap role's permissions editable but silently re-added                                       | Architecture    | Med      | 7o-a          | `SetRolePermissions` refuses it; roles page shows it read-only                   |
| Owner, acting user and requester would be notified of their own action                            | Product         | Med      | 7o-a          | Exclusions in the design and tests                                               |
| Frontend rename guard, EF `Ignore`, comments, broken demo script missing from the plan            | Maintainability | Low      | 7o-a/7o-b     | Added to the table and steps                                                     |

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] CLAUDE.md, README, STATE and agent files no longer mention the removed role names
