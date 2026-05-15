import { Component, OnInit, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, ToastService, StaffService } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, Button, InputText, Message],
  templateUrl: './account.component.html',
  styleUrl:    './account.component.scss',
})
export class AccountComponent implements OnInit {
  private readonly auth      = inject(AuthService);
  private readonly toast     = inject(ToastService);
  private readonly staffSvc  = inject(StaffService);

  phoneNumber   = '';
  savingProfile = signal(false);
  profileError  = signal('');

  ngOnInit(): void {
    this.staffSvc.getMyProfile().subscribe({
      next: profile => { this.phoneNumber = profile.phoneNumber ?? ''; },
    });
  }

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
