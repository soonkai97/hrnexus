# ADR: Leave types are company data, not names in code

> **Status:** Accepted
> **Date:** 2026-11-14
> **Phase:** 7q, `docs/process/phase-07q-leave-types.md`

---

## Context

- The product had five hard-coded leave types (annual, sick, emergency, unpaid, no-show). Every rule that differed between them (which balance is charged, auto-approval, advance notice, who may record it) was a name comparison in about 15 places across the handlers, queries, jobs and both leave pages.
- The owner asked for more types: hospitalisation, maternity, paternity and compassionate. Adding four more by copying the comparisons would have repeated the pattern, and the next company will want different types again. Phase 7m already decided that rules that differ between companies are settings, not constants; Phase 7o decided that behaviour follows permissions, not names.
- An independent review found hazards a naive version would have hit: the 30-day cap would refuse a 98-day maternity request, calendar-day leave split across New Year would be charged the wrong hours (the year split recomputes from the schedule), auto-approval skips the "not your own decision" check (so a configurable flag would let a long absence approve itself), and health facts would reach every approver, push lock screens and the withdrawal and auto-reject messages.

## Decision

A company's leave types are a **catalogue** (`LeaveTypeDefinition`, one row per type). No handler compares a type's name; it finds the definition and asks it.

- **Defaults live in code** (`LeaveTypeDefaults`) and apply until an administrator saves a catalogue, the same way the company policy falls back to its defaults. An untouched installation behaves exactly as before, which the existing leave tests proved by running unchanged.
- A type says: where its hours are charged (`annual`, `sick`, its own company-wide allowance, or no balance), its own day limit (replacing the policy cap), whether it approves itself (only with a balance), its notice, whether it deducts pay, whether only an administrator records it, whether it counts calendar days, and whether it is sensitive.
- **Hours are counted by the type's schedule everywhere** (apply, balances, summary, carry-over, the frontend preview), so a leave is the same number in every view.
- **History is protected:** keys never change; the pool and day counting are locked once a type has leave; a type with leave is deactivated, never removed; sick and no-show are locked system types.
- **Privacy:** a sensitive type is never named in a notification, push, withdrawal or auto-reject message.
- The catalogue is replaced as a whole under one version token (a stale save is a 409), like the company policy. The editor lives on the Company Policy page.
- Payroll (Phase 8c) reads "leave that deducts pay" from the catalogue, never a type name.

## Consequences

- A new company can have its own leave types without a code change. The statutory figures on the new types are assumptions that counsel and the accountant confirm; they are data, so correcting them is a settings change.
- Maternity and paternity eligibility is not modelled (the product stores no gender and no confinement data): a manager checks it and records leave for someone not eligible as `unpaid`.
- The old fixed fields on the balance and summary responses stay next to the new per-type `types[]` list, so existing clients keep working; the leave pages use the list for the new types and keep the fixed cards for the original ones.
- Known limits: the auto-approved notice for emergency leave now reads "Emergency Leave Reported" (it was "Emergency Leave Taken"). Hours of a multi-day leave are recounted from the type's schedule in every view, so leave that has already ended and is marked no-show keeps its stored hours while the views recount it by working days; a retyped leave that has not ended is recounted when saved.
- Deferred: eligibility rules, certificate upload and a reference column (Phase 9), a combined sick and hospitalisation limit (counsel), public holidays and replacement leave, carry-over of own pools.
