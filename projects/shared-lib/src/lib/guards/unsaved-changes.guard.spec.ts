import { TestBed } from '@angular/core/testing';
import { of, Observable } from 'rxjs';
import { unsavedChangesGuard, CanComponentDeactivate } from './unsaved-changes.guard';
import { ConfirmService } from '../services/confirm.service';
import { AuthService } from '../auth/auth.service';

/**
 * unsavedChangesGuard — prompts before navigating away from a page with unsaved form changes.
 *
 * WHY these tests matter:
 *  - It must stay silent during an explicit logout, otherwise the confirm dialog deadlocks behind
 *    the "Signing out…" overlay and the user can't leave.
 *  - It must not nag when there's nothing to lose (clean form) — a spurious prompt is annoying and
 *    trains users to dismiss it, defeating the guard.
 *  - When the form IS dirty it must return the confirm result so a "Cancel" keeps the user on-page.
 */
describe('unsavedChangesGuard', () => {
  let mockAuth: { loggingOut: ReturnType<typeof vi.fn> };
  let mockConfirm: { confirm: ReturnType<typeof vi.fn> };

  const dirty: CanComponentDeactivate = { hasUnsavedChanges: () => true };
  const clean: CanComponentDeactivate = { hasUnsavedChanges: () => false };

  function run(component: CanComponentDeactivate) {
    return TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(component, {} as never, {} as never, {} as never));
  }

  beforeEach(() => {
    mockAuth    = { loggingOut: vi.fn().mockReturnValue(false) };
    mockConfirm = { confirm: vi.fn().mockReturnValue(of(true)) };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService,    useValue: mockAuth },
        { provide: ConfirmService, useValue: mockConfirm },
      ],
    });
  });

  it('allows navigation without prompting during an explicit logout (even if the form is dirty)', () => {
    mockAuth.loggingOut.mockReturnValue(true);
    expect(run(dirty)).toBe(true);
    expect(mockConfirm.confirm).not.toHaveBeenCalled();
  });

  it('allows navigation without prompting when there are no unsaved changes', () => {
    expect(run(clean)).toBe(true);
    expect(mockConfirm.confirm).not.toHaveBeenCalled();
  });

  it('prompts for confirmation and returns the confirm result when the form is dirty', () => {
    const result = run(dirty);
    expect(mockConfirm.confirm).toHaveBeenCalled();
    expect(result).toBe(mockConfirm.confirm.mock.results[0]!.value); // the confirm Observable
  });

  it('keeps the user on the page (false) when they cancel the leave prompt', () => {
    mockConfirm.confirm.mockReturnValue(of(false));
    let emitted: boolean | undefined;
    (run(dirty) as Observable<boolean>).subscribe(v => (emitted = v));
    expect(emitted).toBe(false);
  });
});
