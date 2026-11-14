import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { AuthService } from 'shared-lib';

@Component({
  selector: 'app-admin-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, Button, InputText, Message],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class AdminLoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
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
    this.auth.login({ ...this.form.value, staffId }).subscribe({
      // Any active account may sign in; the shell loads the effective permissions and each route's
      // permissionGuard decides what the user can open.
      next: () => this.router.navigate(['/home']),
      error: () => {
        this.error.set('Invalid Staff ID or password.');
        this.loading.set(false);
      },
    });
  }
}
