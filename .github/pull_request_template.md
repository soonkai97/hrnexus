## What and why
<!-- One or two sentences. Link the phase doc and step (docs/process/phase-NN-*.md) and any issue. -->

## How it was tested
<!-- Commands run and results: dotnet test, npx ng test <project> --no-watch, npx ng build, manual steps. -->

## Checklist
- [ ] Builds clean: `dotnet build backend/src/Api/HRNexus.Api.csproj -c Release -warnaserror` and `npx ng build shared-lib && npx ng build web`
- [ ] New or changed behaviour has tests (handler unit tests, service/store/component specs; E2E where a user flow changed)
- [ ] Coverage scan (CLAUDE.md Rule 17) shows 0 spec gaps; test counts are not below the baseline unless tests were deliberately removed
- [ ] Layering respected (Domain <- Application <- Infrastructure/Api); no logic in controllers
- [ ] **Database migration?** Additive and reversible, or a destructive one approved in the phase doc with a fresh backup and a maintenance window (CLAUDE.md "Production deploy")
- [ ] UI: loading (skeleton), empty and error states; phone width and desktop width in the web app; permission guard on new routes
- [ ] UI text: English only, written inline (no translation keys)
- [ ] No secrets, keys or real credentials in code, config or docs; PII stays encrypted at rest
- [ ] Docs updated (CLAUDE.md, STATE.md, phase doc status)
- [ ] After merge: Docker rebuilt `--no-cache` (CLAUDE.md Rule 18; no deploy workflow exists while there is no production)

## Reviewers
<!-- List the roles that should review (path-to-role table in docs/team/PR-REVIEW.md, section 1). PRs over about 200 lines or touching payroll, auth, migrations, PII or Docker/nginx need an independent review (PR-REVIEW.md, section 8). -->
