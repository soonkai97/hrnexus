import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { AuthService, PermissionService, ThemeToggleComponent, LangService, apiErrorMessage } from 'shared-lib';

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
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  readonly lang           = inject(LangService);

  form: FormGroup;
  loading  = signal(false);
  error    = signal('');
  showPwd  = signal(false);

  // ── Two-factor challenge state ──────────────────────────────────────────────
  twoFactorRequired = signal(false);
  private twoFactorToken = signal('');

  constructor() {
    this.form = this.fb.group({
      staffId:  ['', Validators.required],
      password: ['', Validators.required],
      code:     [''],   // only used during the 2FA step
    });
  }

  removeReadonly(e: Event): void {
    (e.target as HTMLInputElement).removeAttribute('readonly');
  }

  submit(): void {
    if (this.loading()) return;
    if (this.form.get('staffId')?.invalid || this.form.get('password')?.invalid) return;
    const staffId = (this.form.value.staffId as string).trim();
    if (!staffId) { this.error.set(this.translate.instant('auth.errEnterStaffId')); return; }
    this.loading.set(true);
    this.error.set('');
    this.auth.login({ staffId, password: this.form.value.password, portal: 'admin' }).subscribe({
      next: (res) => {
        // 2FA-enabled admin: no session yet — switch to the code-entry step.
        if (res.requiresTwoFactor && res.twoFactorToken) {
          this.twoFactorToken.set(res.twoFactorToken);
          this.twoFactorRequired.set(true);
          this.loading.set(false);
          return;
        }
        this.afterAuthenticated();
      },
      error: (e: unknown) => {
        const msg = apiErrorMessage(e) ?? '';
        this.error.set(
          msg.toLowerCase().includes('admin portal')
            ? this.translate.instant('auth.errAccessDeniedPortal')
            : this.translate.instant('auth.errInvalidCredentials')
        );
        this.loading.set(false);
      },
    });
  }

  verifyCode(): void {
    if (this.loading()) return;
    const code = ((this.form.value.code as string) ?? '').trim();
    if (!code) { this.error.set(this.translate.instant('auth.err2faEnterCode')); return; }
    this.loading.set(true);
    this.error.set('');
    this.auth.verifyTwoFactor(this.twoFactorToken(), code).subscribe({
      next: () => this.afterAuthenticated(),
      error: () => {
        this.error.set(this.translate.instant('auth.err2faInvalid'));
        this.loading.set(false);
      },
    });
  }

  cancelTwoFactor(): void {
    this.twoFactorRequired.set(false);
    this.twoFactorToken.set('');
    this.error.set('');
    this.form.patchValue({ code: '' });
  }

  // Server already enforced portal.admin (401 otherwise). Load the effective permission set so
  // in-app guards/UI work, then double-check portal.admin as defense-in-depth.
  private afterAuthenticated(): void {
    this.permissions.load().subscribe(() => {
      if (!this.permissions.has('portal.admin')) {
        this.auth.logout();
        this.error.set(this.translate.instant('auth.errAccessDeniedPortal'));
        this.loading.set(false);
        return;
      }
      this.router.navigate(['/dashboard']);
    });
  }
}
