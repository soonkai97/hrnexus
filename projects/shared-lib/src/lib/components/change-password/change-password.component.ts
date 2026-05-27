import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Button } from 'primeng/button';
import { Password } from 'primeng/password';
import { Message } from 'primeng/message';
import { AuthService } from '../../auth/auth.service';

@Component({
  selector: 'lib-change-password',
  standalone: true,
  imports: [ReactiveFormsModule, Button, Password, Message],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
})
export class ChangePasswordComponent {
  private readonly fb   = inject(FormBuilder);
  private readonly auth = inject(AuthService);

  form: FormGroup;
  saving  = signal(false);
  success = signal(false);
  error   = signal('');

  constructor() {
    this.form = this.fb.group({
      currentPassword: ['', Validators.required],
      newPassword:     ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required],
    });
  }

  submit(): void {
    const { currentPassword, newPassword, confirmPassword } = this.form.value;
    if (!currentPassword?.trim() || !newPassword?.trim() || !confirmPassword?.trim()) {
      this.error.set('Password fields cannot be blank.');
      return;
    }
    if (newPassword !== confirmPassword) {
      this.error.set('New passwords do not match.');
      return;
    }
    if (this.form.invalid || this.saving()) return;
    this.saving.set(true);
    this.error.set('');
    this.auth.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.saving.set(false);
        this.success.set(true);
        this.form.reset();
        setTimeout(() => { this.success.set(false); }, 1500);
      },
      error: (e) => {
        this.error.set(e?.error?.error ?? 'Failed to change password.');
        this.saving.set(false);
      },
    });
  }
}
