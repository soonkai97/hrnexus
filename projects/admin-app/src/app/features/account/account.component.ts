import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, StaffManagementService, StaffDetail, LangService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { SelectButton } from 'primeng/selectbutton';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [TitleCasePipe, RouterLink, FormsModule, TranslatePipe, Button, Card, Tag, SelectButton],
  templateUrl: './account.component.html',
  styleUrl:    './account.component.scss',
})
export class AccountComponent implements OnInit {
  private readonly auth     = inject(AuthService);
  private readonly staffSvc = inject(StaffManagementService);
  readonly lang             = inject(LangService);

  readonly langOptions = [
    { label: 'English', value: 'en' },
    { label: '中文',    value: 'zh' },
  ];

  user    = computed(() => this.auth.user());
  profile = signal<StaffDetail | null>(null);

  initials = computed(() => {
    const name = this.auth.user()?.fullName ?? '';
    return name.split(' ').filter(Boolean).map(n => n[0]).join('').toUpperCase().slice(0, 2);
  });

  ngOnInit(): void {
    this.staffSvc.getMyProfile().subscribe({
      next: p => this.profile.set(p),
    });
  }

  logout(): void { this.auth.logout(); }
}
