import { Component, signal, inject } from '@angular/core';
import { AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { passwordChecks, isStrongPassword, PasswordChecks } from '../../utils/password.utils';

/** Password policy (single source of truth in password.utils, mirrored on the backend
 *  PasswordPolicy): a minimum length only — no character-class rules. */
export function strongPasswordValidator(ctrl: AbstractControl): ValidationErrors | null {
  const v: string = ctrl.value ?? '';
  if (!v) return null;               // 'required' handles the empty case
  return isStrongPassword(v) ? null : { weakPassword: true };
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
  get pwdChecks(): PasswordChecks {
    return passwordChecks(this.form?.get('newPassword')?.value ?? '');
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
