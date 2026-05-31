import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService, StaffManagementService, StaffDetail, LangService, ToastService, PushService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { SelectButton } from 'primeng/selectbutton';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [RouterLink, FormsModule, TranslatePipe, Button, Card, Tag, SelectButton],
  templateUrl: './account.component.html',
  styleUrl:    './account.component.scss',
})
export class AccountComponent implements OnInit {
  private readonly auth     = inject(AuthService);
  private readonly staffSvc = inject(StaffManagementService);
  private readonly toast    = inject(ToastService);
  private readonly pushSvc  = inject(PushService);
  private readonly translate = inject(TranslateService);
  readonly lang             = inject(LangService);

  readonly langOptions = [
    { label: 'English', value: 'en' },
    { label: '中文',    value: 'zh' },
  ];

  user        = computed(() => this.auth.user());
  profile     = signal<StaffDetail | null>(null);
  pushEnabled = signal(false);
  pushLoading = signal(false);

  initials = computed(() => {
    const name = this.auth.user()?.fullName ?? '';
    return name.split(' ').filter(Boolean).map(n => n[0]).join('').toUpperCase().slice(0, 2);
  });

  ngOnInit(): void {
    this.staffSvc.getMyProfile().subscribe({ next: p => this.profile.set(p) });

    if ('serviceWorker' in navigator && 'PushManager' in window) {
      navigator.serviceWorker.ready.then(reg =>
        reg.pushManager.getSubscription().then(sub => this.pushEnabled.set(!!sub)));
    }
  }

  async togglePush(): Promise<void> {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
      this.toast.error(this.translate.instant('account.pushNotSupported'));
      return;
    }

    this.pushLoading.set(true);
    try {
      const reg = await navigator.serviceWorker.ready;
      const existing = await reg.pushManager.getSubscription();
      if (existing) {
        await existing.unsubscribe();
        await this.pushSvc.unsubscribe(existing.endpoint).toPromise();
        this.pushEnabled.set(false);
        this.toast.success(this.translate.instant('account.pushDisabled'));
      } else {
        const keyRes = await this.pushSvc.getPublicKey().toPromise();
        if (!keyRes?.publicKey) { this.toast.error(this.translate.instant('account.pushNotConfigured')); return; }
        const sub = await this.pushSvc.requestPermission(keyRes.publicKey);
        if (!sub) { this.toast.error(this.translate.instant('account.pushPermissionDenied')); return; }
        const json = sub.toJSON();
        await this.pushSvc.subscribe({
          endpoint: json.endpoint!,
          p256dh:   json.keys?.['p256dh'] ?? '',
          auth:     json.keys?.['auth']   ?? '',
        }).toPromise();
        this.pushEnabled.set(true);
        this.toast.success(this.translate.instant('account.pushEnabled'));
      }
    } catch {
      this.toast.error(this.translate.instant('account.pushToggleFailed'));
    } finally {
      this.pushLoading.set(false);
    }
  }

  logout(): void { this.auth.logout(); }
}
