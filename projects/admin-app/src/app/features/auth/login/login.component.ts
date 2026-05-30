import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { AuthService, ThemeToggleComponent, LangService, apiErrorMessage } from 'shared-lib';

@Component({
  selector: 'app-admin-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, TranslatePipe, Button, InputText, Message],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class AdminLoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
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

  removeReadonly(e: Event): void {
    (e.target as HTMLInputElement).removeAttribute('readonly');
  }

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    const staffId = (this.form.value.staffId as string).trim();
    if (!staffId) { this.error.set(this.translate.instant('auth.errEnterStaffId')); return; }
    this.loading.set(true);
    this.error.set('');
    this.auth.login({ ...this.form.value, staffId, portal: 'admin' }).subscribe({
      next: (res) => {
        const roles = (res.user?.roles ?? []).map(r => r.toUpperCase());
        if (!roles.includes('SUPERADMIN') && !roles.includes('MANAGER')) {
          this.auth.logout();
          this.error.set(this.translate.instant('auth.errAccessDeniedPortal'));
          this.loading.set(false);
          return;
        }
        this.router.navigate(['/dashboard']);
      },
      error: (e: unknown) => {
        const msg = apiErrorMessage(e) ?? '';
        this.error.set(
          msg.includes('Manager or Admin')
            ? this.translate.instant('auth.errAccessDeniedRole')
            : this.translate.instant('auth.errInvalidCredentials')
        );
        this.loading.set(false);
      },
    });
  }
}
