import { Component, signal, inject } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { AuthService, ThemeToggleComponent, LangService } from 'shared-lib';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ThemeToggleComponent, TranslatePipe, Button, InputText, Message],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb     = inject(FormBuilder);
  private readonly auth   = inject(AuthService);
  private readonly router = inject(Router);
  readonly lang           = inject(LangService);
  private readonly translate = inject(TranslateService);

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
    this.auth.login({ ...this.form.value, staffId, portal: 'staff' }).subscribe({
      next: () => {
        this.router.navigate(['/home']);
      },
      error: (e: any) => {
        const raw = e?.error;
        const msg: string = raw?.error ?? raw?.message ?? (typeof raw === 'string' ? raw : '') ?? '';
        this.error.set(
          msg.includes('Admin accounts')
            ? this.translate.instant('auth.errAdminPortal')
            : this.translate.instant('auth.errInvalidCredentials')
        );
        this.loading.set(false);
      },
    });
  }
}
