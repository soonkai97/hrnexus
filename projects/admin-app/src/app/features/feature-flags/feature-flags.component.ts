import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { FeatureFlagService, FeatureKey, ToastService } from 'shared-lib';
import { Button } from 'primeng/button';
import { ToggleSwitch } from 'primeng/toggleswitch';

interface FeatureDef {
  key: FeatureKey;
  icon: string;
  label: string;       // translation key
  description: string; // translation key
  phase: number;
}

@Component({
  selector: 'app-feature-flags',
  standalone: true,
  imports: [FormsModule, TranslatePipe, Button, ToggleSwitch],
  templateUrl: './feature-flags.component.html',
  styleUrl: './feature-flags.component.scss',
})
export class FeatureFlagsComponent {
  readonly featureSvc = inject(FeatureFlagService);
  private readonly toast     = inject(ToastService);
  private readonly translate = inject(TranslateService);

  readonly features: FeatureDef[] = [
    // ── Phase 1 — Finance & Reports ──────────────────────────────────────────
    { key: 'payroll',        icon: 'pi pi-wallet',    phase: 1, label: 'features.payroll.label',         description: 'features.payroll.desc'         },
    { key: 'tips',           icon: 'pi pi-dollar',    phase: 1, label: 'features.tips.label',            description: 'features.tips.desc'            },
    { key: 'paySummary',     icon: 'pi pi-chart-line',phase: 1, label: 'features.paySummary.label',      description: 'features.paySummary.desc'      },
    { key: 'tipsHistory',    icon: 'pi pi-history',   phase: 1, label: 'features.tipsHistory.label',     description: 'features.tipsHistory.desc'     },
    // ── Phase 2 — Communication ──────────────────────────────────────────────
    { key: 'announcements',  icon: 'pi pi-megaphone',  phase: 2, label: 'features.announcements.label',   description: 'features.announcements.desc'   },
    // ── Phase 3 — Staff Ledger ───────────────────────────────────────────────
    { key: 'staffLedger',  icon: 'pi pi-book',       phase: 3, label: 'features.staffLedger.label',   description: 'features.staffLedger.desc'   },
  ];

  readonly phases = [1, 2, 3];

  /** Returns the i18n key for the phase label. */
  phaseLabel(p: number): string {
    return `features.phase${p}Label`;
  }

  featuresForPhase(p: number): FeatureDef[] {
    return this.features.filter(f => f.phase === p);
  }

  isEnabled(key: FeatureKey): boolean {
    return this.featureSvc.isEnabled(key);
  }

  toggle(key: FeatureKey): void {
    this.featureSvc.toggle(key).subscribe({
      error: () => this.toast.error(this.translate.instant('features.toggleFailed')),
    });
  }

  enableAll(): void {
    this.featureSvc.enableAll().subscribe({
      next:  () => this.toast.success(this.translate.instant('features.allEnabled')),
      error: () => this.toast.error(this.translate.instant('features.enableAllFailed')),
    });
  }

  disableAll(): void {
    this.featureSvc.disableAll().subscribe({
      next:  () => this.toast.success(this.translate.instant('features.allDisabled')),
      error: () => this.toast.error(this.translate.instant('features.disableAllFailed')),
    });
  }
}
