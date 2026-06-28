import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService, StaffManagementService, StaffDetail, ToastService, PushService, LangService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { SelectButton } from 'primeng/selectbutton';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [RouterLink, FormsModule, TranslatePipe, Button, Card, Tag, SelectButton],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class ProfileComponent implements OnInit {
  private readonly auth     = inject(AuthService);
  private readonly staffSvc = inject(StaffManagementService);
  private readonly toast    = inject(ToastService);
  private readonly pushSvc  = inject(PushService);
  readonly lang             = inject(LangService);
  private readonly translate = inject(TranslateService);

  readonly langOptions = [
    { label: 'English',     value: 'en' },
    { label: '中文',        value: 'zh' },
    { label: 'Tiếng Việt', value: 'vi' },
  ];

  user         = computed(() => this.auth.user());
  profile      = signal<StaffDetail | null>(null);
  pushEnabled  = signal(false);
  pushLoading  = signal(false);

  initials = computed(() => {
    const u = this.auth.user();
    if (!u) return '';
    // Use fullName from JWT user or fall back to first+last from profile
    const name = u.fullName ?? '';
    return name.split(' ').filter(Boolean).map(n => n[0]).join('').toUpperCase().slice(0, 2);
  });

  ngOnInit(): void {
    this.staffSvc.getMyProfile().subscribe({
      next:  p => this.profile.set(p),
      error: () => {},
    });

    if ('serviceWorker' in navigator && 'PushManager' in window) {
      navigator.serviceWorker.ready.then(reg =>
        reg.pushManager.getSubscription().then(sub => this.pushEnabled.set(!!sub)));
    }
  }

  async togglePush(): Promise<void> {
    // iOS Safari requires the PWA to be installed to the home screen for push notifications
    const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent);
    const isStandalone = ('standalone' in navigator) && (navigator as Navigator & { standalone?: boolean }).standalone;
    if (isIos && !isStandalone) {
      this.toast.warning(this.translate.instant('profile.toast.iosHomeScreen'));
      return;
    }

    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
      this.toast.error(this.translate.instant('profile.toast.pushUnsupported'));
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
      } else {
        const keyRes = await this.pushSvc.getPublicKey().toPromise();
        if (!keyRes?.publicKey) { this.toast.error(this.translate.instant('profile.toast.pushNotConfigured')); return; }
        const sub = await this.pushSvc.requestPermission(keyRes.publicKey);
        if (!sub) { this.toast.error(this.translate.instant('profile.toast.permissionDenied')); return; }
        const json = sub.toJSON();
        await this.pushSvc.subscribe({
          endpoint: json.endpoint!,
          p256dh: json.keys?.['p256dh'] ?? '',
          auth:   json.keys?.['auth']   ?? '',
        }).toPromise();
        this.pushEnabled.set(true);
      }
    } catch {
      this.toast.error(this.translate.instant('profile.toast.enableFailed'));
    } finally {
      this.pushLoading.set(false);
    }
  }

  logout(): void {
    this.auth.logout();
  }
}
