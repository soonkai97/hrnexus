import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService, ThemeToggleComponent } from 'shared-lib';

@Component({
  selector: 'app-set-password',
  standalone: true,
  imports: [CommonModule, FormsModule, ThemeToggleComponent],
  templateUrl: './set-password.component.html',
  styleUrl:    './set-password.component.scss',
})
export class SetPasswordComponent {
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);

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
      next: () => this.router.navigate(['/log-shift']),
      error: (err) => {
        const msg = err?.error?.error ?? 'Failed to set password. Please try again.';
        this.error.set(msg);
        this.loading.set(false);
      },
    });
  }
}
