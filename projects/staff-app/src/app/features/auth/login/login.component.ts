import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, ThemeToggleComponent, LangService } from 'shared-lib';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, TranslatePipe],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);
  readonly lang           = inject(LangService);

  form: FormGroup;
  loading  = signal(false);
  error    = signal('');
  showPwd  = signal(false);

  constructor() {
    this.form = this.fb.group({
      staffId:  ['', Validators.required],
      password: ['', Validators.required],
    });
  }

  /** Removes the readonly guard that prevents browser autofill from populating the field. */
  removeReadonly(e: Event): void {
    (e.target as HTMLInputElement).removeAttribute('readonly');
  }

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.auth.login({ ...this.form.value, portal: 'staff' }).subscribe({
      next: (res) => {
        if (res.mustChangePassword) {
          this.router.navigate(['/auth/set-password']);
        } else {
          this.router.navigate(['/log-shift']);
        }
      },
      error: (e: any) => {
        const raw = e?.error;
        const msg: string = raw?.error ?? raw?.message ?? (typeof raw === 'string' ? raw : '') ?? '';
        this.error.set(
          msg.includes('Admin accounts') ? 'Admin accounts must use the admin portal.' : 'Invalid Staff ID or password.'
        );
        this.loading.set(false);
      },
    });
  }
}
