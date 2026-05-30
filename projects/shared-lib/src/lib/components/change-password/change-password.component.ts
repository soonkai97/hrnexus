import { Component, signal, inject } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

/** Password policy (kept in sync with the backend ChangePasswordCommand): 8+ chars, 1 uppercase, 1 number. */
export function strongPasswordValidator(ctrl: AbstractControl): ValidationErrors | null {
  const v: string = ctrl.value ?? '';
  if (!v) return null;               // 'required' handles the empty case
  const ok = v.length >= 8 && /[A-Z]/.test(v) && /[0-9]/.test(v);
  return ok ? null : { weakPassword: true };
}
import { Button } from 'primeng/button';
import { Password } from 'primeng/password';
import { Message } from 'primeng/message';
import { AuthService } from '../../auth/auth.service';

@Component({
  // eslint-disable-next-line @angular-eslint/component-selector -- legacy 'lib-' selector; renaming would touch both apps' wrapper templates
  selector: 'lib-change-password',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe, Button, Password, Message],
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
})
export class ChangePasswordComponent {
  private readonly fb        = inject(FormBuilder);
  private readonly auth      = inject(AuthService);
  private readonly translate = inject(TranslateService);

  form: FormGroup;
  saving  = signal(false);
  success = signal(false);
  error   = signal('');

  constructor() {
    this.form = this.fb.group({
      currentPassword: ['', Validators.required],
      newPassword:     ['', [Validators.required, strongPasswordValidator]],
      confirmPassword: ['', Validators.required],
    });
  }

  /** Live requirement state for the checklist UI — re-evaluated as the user types. */
  get pwdChecks(): { minLength: boolean; hasUpper: boolean; hasNumber: boolean } {
    const v: string = this.form?.get('newPassword')?.value ?? '';
    return {
      minLength: v.length >= 8,
      hasUpper:  /[A-Z]/.test(v),
      hasNumber: /[0-9]/.test(v),
    };
  }

  submit(): void {
    const { currentPassword, newPassword, confirmPassword } = this.form.value;
    if (!currentPassword?.trim() || !newPassword?.trim() || !confirmPassword?.trim()) {
      this.error.set(this.translate.instant('changePassword.errBlank'));
      return;
    }
    if (newPassword !== confirmPassword) {
      this.error.set(this.translate.instant('changePassword.errMismatch'));
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
        this.error.set(e?.error?.error ?? this.translate.instant('changePassword.errFailed'));
        this.saving.set(false);
      },
    });
  }
}
