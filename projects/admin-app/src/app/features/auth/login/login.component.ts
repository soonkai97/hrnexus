import { Component, signal, inject, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Menu } from 'primeng/menu';
import { AuthService, PermissionService, ThemeToggleComponent, LangService, apiErrorMessage } from 'shared-lib';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-admin-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, TranslatePipe, Button, InputText, Message, Menu],
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

  constructor() {
    this.form = this.fb.group({
      staffId:  ['', Validators.required],
      password: ['', Validators.required],
    });
  }

  submit(): void {
    if (this.form.invalid || this.loading()) return;
    const staffId = (this.form.value.staffId as string).trim().toUpperCase();
    if (!staffId) { this.error.set(this.translate.instant('auth.errEnterStaffId')); return; }
    this.loading.set(true);
    this.error.set('');
    this.auth.login({ ...this.form.value, staffId, portal: 'admin' }).subscribe({
      next: () => {
        // Server already enforced portal.admin (401 otherwise). Load the effective permission set
        // so in-app guards/UI work, then double-check portal.admin as defense-in-depth.
        this.permissions.load().subscribe(() => {
          if (!this.permissions.has('portal.admin')) {
            this.auth.logout();
            this.error.set(this.translate.instant('auth.errAccessDeniedPortal'));
            this.loading.set(false);
            return;
          }
          this.router.navigate(['/dashboard']);
        });
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
}
