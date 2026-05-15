import { Component, signal, inject } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { FeaturePinService } from '../feature-pin.service';
import { Card } from 'primeng/card';
import { Button } from 'primeng/button';

@Component({
  selector: 'app-feature-pin-entry',
  standalone: true,
  imports: [FormsModule, Card, Button],
  templateUrl: './feature-pin-entry.component.html',
  styleUrl:    './feature-pin-entry.component.scss',
})
export class FeaturePinEntryComponent {
  private readonly pinSvc = inject(FeaturePinService);
  private readonly router = inject(Router);

  pin        = '';
  loading    = signal(false);
  error      = signal('');
  showPin    = signal(false);
  retryAfter = signal<Date | null>(null);

  get isLockedOut(): boolean {
    const r = this.retryAfter();
    return !!r && r > new Date();
  }

  get lockoutLabel(): string {
    const r = this.retryAfter();
    if (!r) return '';
    const mins = Math.ceil((r.getTime() - Date.now()) / 60000);
    return `Locked out — try again in ${mins} minute${mins !== 1 ? 's' : ''}.`;
  }

  submit(): void {
    if (!this.pin || this.loading() || this.isLockedOut) return;
    this.error.set('');
    this.loading.set(true);

    this.pinSvc.unlock(this.pin).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/features']);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.pin = '';
        if (err.status === 429) {
          const raw = err.error?.retryAfter;
          this.retryAfter.set(raw ? new Date(raw) : new Date(Date.now() + 15 * 60000));
          this.error.set(this.lockoutLabel);
        } else if (err.status === 503) {
          this.error.set('Feature management PIN is not configured on this server. Please contact support.');
        } else {
          this.error.set('Incorrect PIN. Please try again.');
        }
      },
    });
  }
}
