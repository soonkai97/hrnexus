import { Component, OnInit, signal, inject } from '@angular/core';
import { DecimalPipe, TitleCasePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { StaffStore, ConfirmService, ToastService, TooltipDirective } from 'shared-lib';

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [DecimalPipe, TitleCasePipe, FormsModule, TranslatePipe, TooltipDirective],
  templateUrl: './roles.component.html',
  styleUrl: './roles.component.scss',
})
export class RolesComponent implements OnInit {
  private readonly store      = inject(StaffStore);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly toast      = inject(ToastService);

  readonly roles   = this.store.roles;
  readonly loading = this.store.loading;

  newRoleName = '';
  newRoleRate = 0;
  saving = signal(false);
  error  = signal('');

  // Inline rate editing state
  editingRateName  = signal<string | null>(null);
  editingRateValue = signal(0);
  savingRate       = signal(false);

  ngOnInit(): void { this.store.loadRoles(); }

  addRole(): void {
    const name = this.newRoleName.trim();
    if (!name) return;
    this.saving.set(true);
    this.error.set('');
    this.store.createRole(name, this.newRoleRate).subscribe({
      next: () => { this.newRoleName = ''; this.newRoleRate = 0; this.saving.set(false); this.toast.success('Role created.'); },
      error: (e) => { this.error.set(e?.error?.error ?? 'Failed to create role.'); this.saving.set(false); },
    });
  }

  startEditRate(name: string, currentRate: number): void {
    this.editingRateName.set(name);
    this.editingRateValue.set(currentRate);
  }

  cancelEditRate(): void { this.editingRateName.set(null); }

  saveRate(): void {
    const name = this.editingRateName();
    if (!name || this.savingRate()) return;
    this.savingRate.set(true);
    this.store.updateRoleRate(name, this.editingRateValue()).subscribe({
      next: () => {
        this.savingRate.set(false);
        this.editingRateName.set(null);
        this.toast.success('Hourly rate updated.');
      },
      error: (e) => {
        this.toast.error(e?.error?.error ?? 'Failed to update rate.');
        this.savingRate.set(false);
      },
    });
  }

  deleteRole(name: string): void {
    this.confirmSvc.confirm({
      title: 'Delete Role',
      message: `Delete the "${name.toUpperCase()}" role? This cannot be undone.`,
      confirmLabel: 'Delete',
      danger: true,
    }).subscribe(confirmed => {
      if (!confirmed) return;
      this.store.deleteRole(name).subscribe({
        next: () => this.toast.success('Role deleted.'),
        error: (e) => this.error.set(e?.error?.error ?? 'Failed to delete role.'),
      });
    });
  }
}
