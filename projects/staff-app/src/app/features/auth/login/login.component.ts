import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { AuthService, ThemeToggleComponent, apiErrorMessage } from 'shared-lib';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, Button, InputText, Message],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
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
    this.auth.login({ ...this.form.value, staffId, portal: 'staff' }).subscribe({
      next: () => {
        this.router.navigate(['/home']);
      },
      error: (e: unknown) => {
        const msg = apiErrorMessage(e) ?? '';
        this.error.set(
          msg.includes('Admin accounts')
            ? 'Admin accounts must use the admin portal.'
            : 'Invalid Staff ID or password.'
        );
        this.loading.set(false);
      },
    });
  }
}
