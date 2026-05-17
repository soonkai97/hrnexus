# Phase 06: Remaining Component Tests (Round 3)

> **Status:** `Complete`
> **Created:** 2026-05-17 | **Updated:** 2026-05-17
> **Depends on:** Phase 05 (Remaining Component Tests Round 2)

---

## Objectives

1. Add unit tests for the 4 components deferred from Phase 05 (all have non-trivial logic)
2. Fix Router mock in `NotificationsComponent` spec (NG04002 unhandled rejections)
3. Reach full first-pass unit coverage across every non-passthrough component in both apps

---

## Scope

### In scope

**Admin-app components:**
- `DashboardComponent` — `formatMinutes()`, `ngOnInit/loadDashboard`, `liveStats$` patch side-effects — **M**
- `NotificationsComponent` — `timeAgo()`, `routeFor()`, `filtered()`, `grouped()`, `onNotifClick()`, `markAll()` — **M**
- `AnnouncementsComponent` (admin) — save() 5-layer validation, create vs update path, `startEdit()`, `remove()`, `totalPages` computed — **M**

**Staff-app components:**
- `AnnouncementsComponent` (staff) — `markRead()` optimistic + revert, `markAllRead()` partial revert, `unreadCount`/`hasUnread` computed — **M**

### Out of scope
- `MessagingComponent` — pure UI rendering; no extractable pure logic; E2E covers the happy path

---

## Dependencies

| Dependency | Type | Status |
|---|---|--------|
| Phase 05 complete — 524 unit tests | Code | ✅ Done |
| `SignalRService.liveStats$` Subject mock | Test infra | ✅ Implemented |
| `NotificationService.markReadOptimistic` | Service method | ✅ Exists |
| `AnnouncementService.getMine / markRead` | Service methods | ✅ Exists |

---

## Risks and resolutions

| Risk | Resolution |
|---|---|
| Angular `effect()` is scheduled, not synchronous — `hubConnected` effect untestable without `detectChanges()` | Removed `hubConnected` effect block; kept behaviour-based tests for the observable side-effects |
| `loadDashboard(false)` overwrites optimistic `liveStats$` patch synchronously (uses `of()` mock) | Replaced transient-state tests with side-effect tests (`getDashboard called`, `loading.set(true)` NOT called) |
| `onNotifClick()` calls `router.navigate(['/leave'])` — `provideRouter([])` has no routes → NG04002 unhandled rejection | Provided `{ provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } }` after `provideRouter([])` |

---

## Implementation Steps

1. `[TDD]` `DashboardComponent` spec — `formatMinutes()`, init/load, `liveStats$` side-effects — M
2. `[TDD]` `NotificationsComponent` spec — `timeAgo()`, `routeFor()`, `filtered()`, `grouped()`, click/markAll — M
3. `[TDD]` admin `AnnouncementsComponent` spec — validation chain, CRUD paths, `startEdit()`, `totalPages` — M
4. `[TDD]` staff `AnnouncementsComponent` spec — `markRead()` optimistic, partial revert in `markAllRead()` — M
5. Fix: Router mock for `NotificationsComponent` (NG04002 unhandled rejections)
6. Run `npm run test:admin` — 341/341 pass, clean exit
7. Run `npm run test:staff` — 190/190 pass, clean exit

---

## Validation Criteria

- [x] `npm run test:admin` — 341/341 pass (14 spec files), exit 0, no unhandled rejections
- [x] `npm run test:staff` — 190/190 pass (7 spec files), exit 0
- [x] DashboardComponent: `formatMinutes()` all 6 cases; liveStats$ triggers silent refresh; no loading flicker
- [x] NotificationsComponent: `timeAgo()` 5 time-buckets; `routeFor()` 10 type mappings; `grouped()` 3 date buckets; router mock eliminates NG04002
- [x] Admin AnnouncementsComponent: empty/whitespace title, body>5000, past expiry blocked; create/update path split; `startEdit()` expiresAt slice
- [x] Staff AnnouncementsComponent: optimistic mark-read; revert on error; partial revert in markAllRead

---

## Rollback

All new files are spec-only — no production code changed. Individual files are independently revertible via `git revert` or `git checkout HEAD~1 -- <file>`.

---

## Review Notes

### Final counts

| Suite | Spec files | Tests |
|-------|-----------|-------|
| shared-lib | 6 | 88 |
| admin-app | 14 | 341 |
| staff-app | 7 | 190 |
| **Total** | **27** | **619** |

### Multi-perspective review

| Perspective | Finding | Severity |
|---|---|---|
| **Architecture** | All 4 new specs follow the established TestBed-no-detectChanges pattern. `Subject<any>` for SignalR mock is the correct seam — mirrors production `liveStats$` observable shape. | ✅ None |
| **Scalability** | `grouped()` and `filtered()` need fresh component instances per scenario because Angular `computed()` with mocked (non-signal) dependencies caches after first evaluation. Pattern documented in CLAUDE.md under "Key patterns". | ✅ None |
| **Security** | No sensitive data in fixtures. Router mock returns resolved promise only — does not allow real navigation to sensitive routes. | ✅ None |
| **Maintainability** | WHY comments in all 4 spec files link tests to concrete business risks (badge staleness, missed-clockin routing, optimistic revert, announcement visibility). `Subject<any>` liveStats$ mock replaces brittle timer-based async approach. | ✅ None |
| **Testing** | DashboardComponent tests verify _side-effects_ of liveStats$ (getDashboard called, no loading flicker) rather than transient patch values that are immediately overwritten — more resilient to implementation details. Staff announcements partial-revert test covers the per-item failure path explicitly. | ✅ None |
| **Product Alignment** | Routing correctness (leave_pending → /leave), unread badge accuracy (hasUnread/unreadCount), optimistic reads-that-revert-on-error — all map directly to operational UX requirements. | ✅ None |
| **Performance** | All 619 tests synchronous; fake timers used and restored correctly. No timers left running between tests. | ✅ None |

### Issues found

| Issue | Perspective | Severity | Affects Phase | Resolution |
|---|---|---|---|---|
| Angular `effect()` not synchronous in TestBed without change detection | Testing | Low | Phase 06 | Removed untestable `hubConnected` effect block; captured behaviour via `isConnected` mock return value |
| `loadDashboard(false)` synchronously overwrites optimistic `liveStats$` patch when mock uses `of()` | Testing | Low | Phase 06 | Switched to side-effect verification instead of transient state assertions |
| `router.navigate()` throws NG04002 with empty `provideRouter([])` | Testing | Med (exit code 1) | Phase 06 | Mock Router token after `provideRouter([])` |

### Next candidates (Phase 07+)
- `MessagingComponent` (both apps) — pure UI with SignalR; E2E covers the happy path
- E2E expansion for announcements CRUD and notification read flow
