import { signal, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Shared logic for the set-password (forced password change) page.
 * Extend this class and set `redirectPath` to the post-login route for each app.
 */
export abstract class SetPasswordBase {
  /** The route to navigate to after a successful password change. */
  protected abstract readonly redirectPath: string;

  protected readonly auth   = inject(AuthService);
  protected readonly router = inject(Router);

  newPwd      = '';
  confirmPwd  = '';
  loading     = signal(false);
  error       = signal('');
  showNew     = signal(false);
  showConfirm = signal(false);

  submit(): void {
    this.error.set('');
    if (this.newPwd.length < 8) {
      this.error.set('Password must be at least 8 characters.');
      return;
    }
    if (this.newPwd !== this.confirmPwd) {
      this.error.set('Passwords do not match.');
      return;
    }
    if (this.loading()) return;
    this.loading.set(true);
    this.auth.forceChangePassword(this.newPwd).subscribe({
      next: () => this.router.navigate([this.redirectPath]),
      error: (err: any) => {
        const msg = err?.error?.error ?? 'Failed to set password. Please try again.';
        this.error.set(msg);
        this.loading.set(false);
      },
    });
  }
}
