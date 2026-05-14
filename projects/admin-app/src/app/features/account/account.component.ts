import { Component, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, ToastService } from 'shared-lib';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './account.component.html',
  styleUrl:    './account.component.scss',
})
export class AccountComponent {
  private readonly auth  = inject(AuthService);
  private readonly toast = inject(ToastService);

  phoneNumber   = '';
  savingProfile = signal(false);
  profileError  = signal('');

  saveProfile(): void {
    this.savingProfile.set(true);
    this.profileError.set('');
    this.auth.updateProfile(this.phoneNumber || null).subscribe({
      next: () => { this.savingProfile.set(false); this.toast.success('Profile updated.'); },
      error: () => {
        this.profileError.set('Failed to update profile.');
        this.toast.error('Failed to update profile.');
        this.savingProfile.set(false);
      },
    });
  }
}
