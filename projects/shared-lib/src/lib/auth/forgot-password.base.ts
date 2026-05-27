import { signal, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { LangService } from '../services/lang.service';

/**
 * Shared logic for the forgot-password page.
 * Both admin-app and staff-app extend this class; each supplies its own template and SCSS.
 */
export abstract class ForgotPasswordBase {
  protected readonly auth = inject(AuthService);
  readonly lang           = inject(LangService);

  staffId = '';
  loading = signal(false);
  error   = signal('');
  sent    = signal(false);

  submit(): void {
    if (!this.staffId?.trim() || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.auth.forgotPassword(this.staffId.trim()).subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.found) {
          this.sent.set(true);
        } else {
          this.error.set('Staff ID not found. Please check and try again.');
        }
      },
      error: () => {
        this.error.set('Something went wrong. Please try again.');
        this.loading.set(false);
      },
    });
  }
}
