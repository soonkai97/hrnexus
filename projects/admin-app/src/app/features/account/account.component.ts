import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService, StaffManagementService, StaffDetail, LangService, ToastService, PushService,
  TwoFactorService, TwoFactorEnrollInfo, apiErrorMessage } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';
import { Tag } from 'primeng/tag';
import { SelectButton } from 'primeng/selectbutton';
import { InputText } from 'primeng/inputtext';

@Component({
  selector: 'app-account',
  standalone: true,
  imports: [RouterLink, FormsModule, TranslatePipe, Button, Card, Tag, SelectButton, InputText],
  templateUrl: './account.component.html',
  styleUrl:    './account.component.scss',
})
export class AccountComponent implements OnInit {
  private readonly auth     = inject(AuthService);
  private readonly staffSvc = inject(StaffManagementService);
  private readonly toast    = inject(ToastService);
  private readonly pushSvc  = inject(PushService);
  private readonly twoFactor = inject(TwoFactorService);
  private readonly translate = inject(TranslateService);
  readonly lang             = inject(LangService);

  // ── Two-factor authentication state ─────────────────────────────────────────
  twoFaEnabled   = signal(false);
  twoFaLoading   = signal(false);
  enrollInfo     = signal<TwoFactorEnrollInfo | null>(null);   // QR + secret while enrolling
  recoveryCodes  = signal<string[] | null>(null);              // shown once after confirm
  disablingTwoFa = signal(false);                              // toggles the disable code field
  twoFaCode = '';

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
    this.twoFactor.status().subscribe({ next: s => this.twoFaEnabled.set(s.enabled) });

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

  // ── Two-factor authentication ───────────────────────────────────────────────
  startEnroll(): void {
    this.twoFaLoading.set(true);
    this.twoFactor.beginEnroll().subscribe({
      next: info => { this.enrollInfo.set(info); this.twoFaCode = ''; this.twoFaLoading.set(false); },
      error: e => { this.toast.error(apiErrorMessage(e) ?? this.translate.instant('account.twoFaError')); this.twoFaLoading.set(false); },
    });
  }

  confirmEnroll(): void {
    const code = this.twoFaCode.trim();
    if (!code) return;
    this.twoFaLoading.set(true);
    this.twoFactor.confirmEnroll(code).subscribe({
      next: res => {
        this.recoveryCodes.set(res.recoveryCodes);
        this.twoFaEnabled.set(true);
        this.enrollInfo.set(null);
        this.twoFaCode = '';
        this.twoFaLoading.set(false);
        this.toast.success(this.translate.instant('account.twoFaEnabled'));
      },
      error: e => { this.toast.error(apiErrorMessage(e) ?? this.translate.instant('account.twoFaCodeInvalid')); this.twoFaLoading.set(false); },
    });
  }

  cancelEnroll(): void { this.enrollInfo.set(null); this.twoFaCode = ''; }

  startDisable(): void { this.disablingTwoFa.set(true); this.twoFaCode = ''; }
  cancelDisable(): void { this.disablingTwoFa.set(false); this.twoFaCode = ''; }

  confirmDisable(): void {
    const code = this.twoFaCode.trim();
    if (!code) return;
    this.twoFaLoading.set(true);
    this.twoFactor.disable(code).subscribe({
      next: () => {
        this.twoFaEnabled.set(false);
        this.disablingTwoFa.set(false);
        this.twoFaCode = '';
        this.twoFaLoading.set(false);
        this.toast.success(this.translate.instant('account.twoFaDisabled'));
      },
      error: e => { this.toast.error(apiErrorMessage(e) ?? this.translate.instant('account.twoFaCodeInvalid')); this.twoFaLoading.set(false); },
    });
  }

  dismissRecoveryCodes(): void { this.recoveryCodes.set(null); }

  logout(): void { this.auth.logout(); }
}
