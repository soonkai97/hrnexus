# Phase 7i: English only (remove multi-lingual support)

> **Status:** `Complete (implemented on feat/english-only; DropMultilingualFields migration generated)`
> **Created:** 2026-11-13 | **Updated:** 2026-11-13
> **Depends on:** Phase 7 (remove shift log) merged, **Phase 7j (remove Locations) merged**. Phase 7g (Nx workspace) starts after this.
> **Environment note (13 Nov 2026):** no production deployment exists, so the backup, approval and maintenance-window steps below apply to the next deployment that holds data.

---

## Objectives

1. UI text is inline English. No `ngx-translate`, `LangService`, language switcher, `/i18n/*.json` or `LocalizedNamePipe` remain in either app or in `shared-lib`.
2. The API and DB carry no multilingual fields: `Role.NameZh/NameVi`, `Staff.PreferredLanguage`, the language endpoints and their DTO fields are gone (user confirmed this scope).
3. A guard test fails the build if `translate`/`TranslatePipe`/`TranslateService` or `*Zh`/`*Vi` identifiers reappear.
4. Behaviour is unchanged for English users: same text, same tests green, no regression in E2E selectors.
5. The deviation from the IMS standard (runtime i18n with typed keys) is recorded in an ADR.

---

## Measured footprint (at Phase 7 commit `9f8aa3c`; Phase 7j has already removed the Location parts)

| Area | Count |
|---|---|
| Template pipe `\| translate` | 20 `.html` files (admin 12, staff 6, shared-lib 2), **474** matches (admin 403, staff 104, shared-lib 9 incl. inline templates in `app.ts`, `toast`, `confirm-dialog`) |
| `translate.instant/get/stream` in non-spec `.ts` | 16 files, **224** matches |
| Non-spec `.ts` importing `TranslatePipe/TranslateService/TranslateModule` | 23 files |
| Specs providing `provideTranslateService` | 21 files (admin 14, staff 6, shared-lib 3); 14 specs also assert on `nameZh`/keys/`LangService` |
| App providers | `provideTranslateService` + `provideTranslateHttpLoader` in both `app.config.ts`; deps `@ngx-translate/core` + `http-loader` in `package.json` |
| Keys with params (`translate: {…}`) | 14 matches in 2 template files; 42 en keys contain `{{ }}` (admin 39, staff 3) |
| **Dynamic keys** (need hand work) | 10 template expressions (`'x.' + y \| translate`), 2 `instant()` calls with computed keys, ~24 `labelKey`/`*Key` fields (nav items, menus, status maps) |
| Locale files | admin `en/zh/vi` = **532** keys each; staff `en/zh/vi` = **159** keys each (Phase 7 already cut 737 → 532 and 218 → 159) |
| `LangService` | used in 21 files (shell x2, login x2, forgot-password x2 + base, account, profile, staff, staff-detail, specs); persists `localStorage` lang key; globe toggle in both shells, both login pages, both forgot-password pages, account + profile language cards |
| `LocalizedNamePipe` / `localizedNameFor` | 2 template uses, used in `roles`, `staff`, `staff-detail` components; exported from `shared-lib` `public-api.ts` (lines for `lang.service`, `localized-name.pipe`) |
| Backend `*Zh/*Vi` | 5 files, 17 matches in `src` (after Phase 7j): `Role.cs`, `RoleConfiguration`, `CreateRoleCommand`, `GetRolesQuery`, `StaffController`; 0 in `tests` |
| Backend language preference | `Staff.PreferredLanguage` + `SetLanguage`, `UpdateLanguageCommand` (whitelist `["en","zh","vi"]`), `AuthController` `GetLanguage`/`UpdateLanguage`/`UpdateLanguageBody` |
| Migrations that created the fields (history only) | `AddPreferredLanguage`, `AddBilingualNames`, `AddVietnameseNames` |
| Server-side localization | none: no `IStringLocalizer`, no `Accept-Language`, no localization helpers. Push/notification text is built in English on the server; the FE bell maps notification types through keys |
| Seed data translations | none in code (only EF column mappings) |
| PrimeNG locale | no `setTranslation`; PrimeNG already runs on its English defaults |
| Ops | 4 nginx `location ~* /i18n/` blocks (`nginx.conf` x2, `nginx.uat.conf` x2) |
| E2E | ~19 text assertions in 9 files (English text, unaffected); `staff/profile.spec.ts` "language selector (EN / ZH toggle)" test must be deleted |
| Docs | CLAUDE.md i18n section (lines ~462-498) + refs at ~85, 107, 143, 246, 604, 1247-1255; also README, `REVIEW_CHECKLIST.md` |

---

## Scope

### In scope
- Frontend: codemod all templates and TS to literal English; delete `LangService`, `LocalizedNamePipe`, language UI (globe buttons, account/profile language cards), locale JSON, loaders, nginx `/i18n/` blocks, `ngx-translate` deps.
- Backend: remove `Role.NameZh/NameVi`, `Staff.PreferredLanguage`, `UpdateLanguageCommand`, the two language endpoints, whitelist, DTO fields (`CreateRoleCommand`, `GetRolesQuery`, `StaffController`, login/profile DTOs).
- DB: one drop migration (irreversible).
- Tests, E2E, docs, ADR.

### Out of scope
- **Location `NameZh/NameVi`**: Phase 7j removes the whole Locations feature first, so those columns are already gone; this migration only touches `Roles` and `Staff`.
- Date/number formatting (Angular `DatePipe` stays on `en-US`; HmPipe unchanged).
- Re-adding any i18n later (would be a new phase + ADR).

---

## Strategy: mechanical codemod, not hand edits

A throwaway Node script (kept out of the repo, or under `scripts/oneoff/` deleted in the same PR) run per app against that app's `en.json`:
1. Resolve `{{ 'a.b' | translate }}`, `[attr]="'a.b' | translate"`, `attr="{{ 'a.b' | translate }}"` to the English literal (escape `{`, `}`, quotes; keep Angular binding syntax where the value is bound).
2. Resolve `{{ 'a.b' | translate: { n: x } }}` and `instant('a.b', { n })` by substituting `{{n}}` with `{{ x }}` in templates or `${x}` template literals in TS.
3. Resolve `this.translate.instant('a.b')` to a string literal.
4. Remove `TranslatePipe`/`TranslateService` imports, `inject(TranslateService)`, `provideTranslateService` from specs and configs; drop now-unused imports (run `eslint --fix`).
5. Emit a report: unresolved keys (not in en.json), **dynamic keys**, plural/param cases, files changed. Dynamic keys (10 + 2 + ~24 `*Key` fields) are converted by hand: `labelKey` becomes `label`; `'status.' + s` becomes a typed `Record<Status, string>` constant next to the component.
6. After the run: `npx ng build` per project; diff-review the report; spot-check 10 pages in both apps in the browser.

Order of work keeps each commit buildable: codemod admin-app, codemod staff-app, codemod shared-lib, then delete the infrastructure once nothing imports it.

---

## Implementation Steps

1. `[TDD]` Add the guard test first (fails today): a Vitest/Node test scanning `projects/**` for `ngx-translate`, `TranslatePipe`, `LangService`, `LocalizedNamePipe`, `nameZh|nameVi|preferredLanguage`, and a backend architecture/grep test for `NameZh|NameVi|PreferredLanguage|UpdateLanguage` — S
2. Snapshot English text: render-text snapshot of key pages is optional; cheaper proof = the codemod report + existing component specs asserting on English strings — S
3. Write and dry-run the codemod; review the unresolved/dynamic report — M
4. Run codemod on admin-app; hand-fix dynamic keys; build + `ng test admin-app` — M
5. Same for staff-app — S
6. shared-lib: `notification-bell`, `toast`, `confirm-dialog`, `theme-toggle`, `forgot-password.base`; remove `LangService`, `LocalizedNamePipe`, exports — S
7. Delete language UI (globe buttons x6 places, account/profile language cards), locale JSON (12 files), loader config, `package.json` deps, nginx `/i18n/` blocks; `npm install` to refresh the lockfile — S
8. Backend code: remove fields, endpoints, command, whitelist, DTO members; fix tests; keep build green while the model snapshot still has the columns — M
9. **Migration `DropMultilingualFields`** (drops `Roles.NameZh`, `Roles.NameVi`, `Staff.PreferredLanguage`) — S. Stop and ask the user first.
10. Update E2E (delete language-selector test), `scripts` if any seed `nameZh`, CLAUDE.md (delete i18n section + the "add keys to all locale files" rule, update `app.config`/`LangService` mentions, test pattern snippets), README, `REVIEW_CHECKLIST.md`, STATE.md — S
11. ADR `docs/architecture/adr-english-only.md`: deliberate deviation from IMS runtime i18n; reasons (single-language venue, remove 700+ keys x3 upkeep); how to reverse (new phase, typed keys per IMS) — S
12. Full verification, PR, squash-merge, Docker `--no-cache`, deploy in a maintenance window — M

Estimate: **about 3 days** (codemod + review 1.5, backend + migration 0.5, tests/docs/ADR 0.5, verification 0.5). Hand-editing instead would take 2-3x and is not recommended.

---

## DB migration and API contract

- **Irreversible:** the migration drops stored Chinese/Vietnamese role names and each staff member's language preference. Needs the user's explicit approval, a fresh `scripts/backup-db.sh` backup and a maintenance window, same rule as the WorkLogs drop. Option offered to the user: export non-null `Roles.NameZh/NameVi` to CSV first.
- **Sequencing inside the migration:** run after Phase 7 and 7j migrations so the model snapshot is clean; Location columns are already dropped by 7j.
- **Breaking API changes** (FE and BE ship together): role DTOs lose `nameZh/nameVi`; role create/update bodies lose them; `GET/PUT /api/auth/language` (or profile language handling) removed; login/profile DTOs lose `preferredLanguage`. A stale PWA cache calling the removed endpoints gets a 404: SW update + hard refresh after deploy.

---

## Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Strings with params or plural forms mis-converted | Med | Med | Codemod handles `{{n}}`; report lists every param key; review diff of the 14 + 42 cases |
| Dynamic keys (`'x.' + y`, `*Key` fields) missed, shows raw key at runtime | Med | High | Codemod flags every non-literal; guard test + grep for `'[a-z]+\.[a-zA-Z.]+'` shaped literals passed to removed APIs; page spot-checks |
| E2E selectors match text that the codemod changed (e.g. a key missing in en.json) | Low | Med | Run `npm run test:e2e` against rebuilt Docker; text comes from `en.json`, already what tests see |
| Inline templates (`app.ts`, toast, confirm-dialog) missed by an `.html`-only scan | Med | Med | Codemod scans `.ts` `template:` strings too |
| Server push/SignalR text | Low | Low | Already English on the server (no localizer); only FE type-to-text keys change |
| PrimeNG built-in labels | Low | Low | No `setTranslation` today; stays English |
| Merge conflicts with Phase 7 / 7j in locale files | High if run in parallel | Med | Strict sequencing: start only after 7 and 7j merge |
| Irreversible data loss (translations, language preference) | Certain | Low-Med | Backup + optional CSV export + approval gate |
| Later need for a second language | Low | High | ADR documents the reversal path |

---

## Sequencing

1. Phase 7 (shift log) merges, then **Phase 7j (Locations removal)**, then this phase. Both earlier phases already shrink the locale JSONs and the Location `*Zh/*Vi` columns, so this phase works on the smaller final surface.
2. Before Phase 7g (Nx workspace): doing the codemod first avoids moving thousands of translated lines twice.
3. Relation to Phase 7d (ProblemCodes): the FE `ApiError` mapping needs English messages per code; write that mapping as a plain typed constant, not i18n keys. If 7d lands first, include its message table in the codemod scope.
4. Relation to the IMS alignment doc: IMS has runtime i18n with typed message keys and a catalog-completeness test, so the alignment doc's "i18n completeness test" row (7g) becomes "not applicable". Update that row when this phase is approved.

---

## Validation Criteria

- [ ] Guard test: no `ngx-translate`, `TranslatePipe`, `TranslateService`, `LangService`, `LocalizedNamePipe`, `nameZh`, `nameVi`, `preferredLanguage` anywhere under `projects/` and `backend/src` (migrations and model-history excluded)
- [ ] `npx ng build` clean for shared-lib, admin-app, staff-app; no new lint errors
- [ ] `npx ng test` green for all three; test count drops only by removed language specs (`lang.service.spec`, `localized-name.pipe.spec`, language cases in login/profile/shell/account)
- [ ] `dotnet test` green; migration applies on a copy of prod data
- [ ] Browser spot-check of every top-level page in both apps: no raw keys (`a.b.c` patterns) visible; screenshots compared to master for 6 key pages
- [ ] E2E green against rebuilt Docker
- [ ] `package.json` has no `@ngx-translate/*`; `public/i18n` folders deleted; nginx `/i18n/` blocks removed
- [ ] CLAUDE.md contains no i18n rules; ADR written
- [ ] Multi-perspective review (`REVIEW_CHECKLIST.md`)

---

## Rollback

- **Git:** revert the squash-merge commit (restores code and locale files).
- **Data:** restore the pre-migration backup via `scripts/restore-db.sh` to bring back `NameZh/NameVi/PreferredLanguage` values. Without the backup (or CSV export) those values cannot be recovered.

---

## Open questions

1. Export the stored `Roles.NameZh/NameVi` to CSV before dropping them, or accept the loss?
2. Keep the date format as `en-US` (DD/MM vs MM/DD currently follows the app's existing patterns), or standardize on `dd/MM/yyyy` while touching every template?
3. Is a single language permanent for this product, or should the ADR reserve the IMS typed-key approach as the reversal path (recommended, no code cost)?

---

## Sign-off

- [ ] All validation criteria met
- [ ] Review completed (all 7 perspectives)
- [ ] Phase 7g plan updated (i18n completeness test removed)
- [x] CLAUDE.md updated (i18n section deleted, new rule: English literals only)

---

## Review Notes

- Dynamic keys were converted by a one-off codemod plus hand edits; guards added in both stacks.
