import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { ConfirmService } from '../services/confirm.service';

/** Implement this on any component that uses unsavedChangesGuard. */
export interface CanComponentDeactivate {
  hasUnsavedChanges(): boolean;
}

/**
 * Route guard that prompts the user before navigating away from a page
 * with unsaved form changes.  The component must implement hasUnsavedChanges().
 */
export const unsavedChangesGuard: CanDeactivateFn<CanComponentDeactivate> = (component) => {
  if (!component.hasUnsavedChanges()) return true;
  return inject(ConfirmService).confirm({
    title: 'Unsaved Changes',
    message: 'You have unsaved changes. Leaving this page will discard them.',
    confirmLabel: 'Leave',
    danger: false,
  });
};
