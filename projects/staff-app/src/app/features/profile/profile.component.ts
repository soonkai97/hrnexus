import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { TitleCasePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService, StaffService, StaffDetail, ToastService, PushService, LangService } from 'shared-lib';
import { Button } from 'primeng/button';
import { Tag } from 'primeng/tag';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [TitleCasePipe, RouterLink, TranslatePipe, Button, Tag],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss',
})
export class ProfileComponent implements OnInit {
  private readonly auth     = inject(AuthService);
  private readonly staffSvc = inject(StaffService);
  private readonly toast    = inject(ToastService);
  private readonly pushSvc  = inject(PushService);
  readonly lang             = inject(LangService);

  user     = computed(() => this.auth.user());
  profile  = signal<StaffDetail | null>(null);
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
        if (!keyRes?.publicKey) { this.toast.error('Push not configured.'); return; }
        const sub = await this.pushSvc.requestPermission(keyRes.publicKey);
        if (!sub) { this.toast.error('Permission denied.'); return; }
        const json = sub.toJSON();
        await this.pushSvc.subscribe({
          endpoint: json.endpoint!,
          p256dh: json.keys?.['p256dh'] ?? '',
          auth:   json.keys?.['auth']   ?? '',
        }).toPromise();
        this.pushEnabled.set(true);
      }
    } catch (e) {
      this.toast.error('Push notification error.');
    } finally {
      this.pushLoading.set(false);
    }
  }

  logout(): void { this.auth.logout(); }
}
