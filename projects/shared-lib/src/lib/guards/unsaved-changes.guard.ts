import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmService } from '../services/confirm.service';
import { AuthService } from '../auth/auth.service';

/** Implement this on any component that uses unsavedChangesGuard. */
export interface CanComponentDeactivate {
  hasUnsavedChanges(): boolean;
}

/**
 * Route guard that prompts the user before navigating away from a page
 * with unsaved form changes.  The component must implement hasUnsavedChanges().
 */
export const unsavedChangesGuard: CanDeactivateFn<CanComponentDeactivate> = (component) => {
  // Don't nag (or deadlock behind the "Signing out…" overlay) during an explicit logout —
  // the user chose to leave, so discard any unsaved form state silently.
  if (inject(AuthService).loggingOut()) return true;
  if (!component.hasUnsavedChanges()) return true;
  return inject(ConfirmService).confirm({
    title: 'Unsaved Changes',
    message: 'You have unsaved changes. Leaving this page will discard them.',
    confirmLabel: 'Leave',
    danger: false,
  });
};
