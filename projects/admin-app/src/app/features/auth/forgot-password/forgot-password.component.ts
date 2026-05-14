import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, ThemeToggleComponent } from 'shared-lib';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ThemeToggleComponent, TranslatePipe],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);

  staffId = '';
  loading = signal(false);
  error   = signal('');
  sent    = signal(false);

  submit(): void {
    if (!this.staffId || this.loading()) return;
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
      error: () => { this.error.set('Something went wrong. Please try again.'); this.loading.set(false); },
    });
  }
}
