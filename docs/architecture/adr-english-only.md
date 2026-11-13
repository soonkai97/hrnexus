# ADR: English only (no multi-language support)

> **Status:** Accepted
> **Date:** 2026-11-13
> **Phase:** 7i, `docs/process/phase-07i-english-only.md`

---

## Context

- HRNexus serves a single-language venue in Malaysia. Staff and managers work in English.
- The apps shipped three locales (`en`, `zh`, `vi`): roughly 700 keys per locale, so about 700 x 3 strings to keep in step in every change.
- Runtime i18n (ngx-translate) added cost beyond the string upkeep: string keys are untyped, dynamic keys (`'x.' + y | translate`, `labelKey` fields) failed silently at runtime, and every component spec needed translation providers.
- Backend multilingual data (`Role.NameZh/NameVi`, `Staff.PreferredLanguage`) existed only to feed the language switcher.

## Decision

The product is **English only**. UI text is inline English literals in templates and components.

Removed:

- `ngx-translate` (`@ngx-translate/core` and the http loader), translate pipes and services, `/i18n/*.json` locale files and the nginx `/i18n/` blocks
- `LangService`, `LocalizedNamePipe` and the language switcher UI (topbar globe, account and profile language cards)
- `Role.NameZh/NameVi`, `Staff.PreferredLanguage`, `UpdateLanguageCommand` and the language endpoints with their DTO fields
- Database columns, dropped by migration `DropMultilingualFields`

## Consequences

- Less code and fewer moving parts: no key catalog, no loader, no translation providers in specs.
- No translation drift between locales.
- **Irreversible data drop:** stored Chinese and Vietnamese role names and every staff member's language preference are deleted by the migration. Restoring them needs a pre-migration backup (`scripts/restore-db.sh`).
- E2E selectors are unchanged: the English text they match is the same text, now inline.
- A second language later is a new project, not a toggle (see "How to reverse").

## Deviation from the IMS standard

The IMS reference standard (github.com/soonkai97/IMS) has runtime i18n with typed message keys and a catalog-completeness test. HRNexus deliberately does not follow it here. The row in `docs/process/architecture-alignment-ims.md` is marked not applicable.

## How to reverse

1. Write a new phase document and a new ADR that supersedes this one.
2. Follow the IMS typed-key approach (typed message keys plus a catalog-completeness test), not ngx-translate string keys.
3. Reintroduce language data (columns, endpoints, preference) in the same phase, with its own migration.

## Guards

Two tests fail the build if the removed identifiers reappear:

- Frontend: `projects/shared-lib/src/lib/english-only.guard.spec.ts`
- Backend: `backend/tests/Application.Tests/EnglishOnlyGuardTests.cs`
