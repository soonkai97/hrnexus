import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { AuthService, PermissionService, ThemeToggleComponent, apiErrorMessage } from 'shared-lib';

@Component({
  selector: 'app-admin-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, Button, InputText, Message],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class AdminLoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);

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
    if (!staffId) { this.error.set('Please enter your Staff ID.'); return; }
    this.loading.set(true);
    this.error.set('');
    this.auth.login({ ...this.form.value, staffId, portal: 'admin' }).subscribe({
      next: () => {
        // Server already enforced portal.admin (401 otherwise). Load the effective permission set
        // so in-app guards/UI work, then double-check portal.admin as defense-in-depth.
        this.permissions.load().subscribe(() => {
          if (!this.permissions.has('portal.admin')) {
            this.auth.logout();
            this.error.set('Access denied. Only Admin or Manager accounts can log in to this portal.');
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
            ? 'Access denied. Only Admin or Manager accounts can log in to this portal.'
            : 'Invalid Staff ID or password.'
        );
        this.loading.set(false);
      },
    });
  }
}
