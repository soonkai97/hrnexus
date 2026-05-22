import { Component, OnInit, signal, inject, ViewChild, ElementRef } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { StaffStore, ConfirmService, ToastService } from 'shared-lib';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { InputNumber } from 'primeng/inputnumber';
import { TableModule } from 'primeng/table';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { TooltipModule } from 'primeng/tooltip';
import { Message } from 'primeng/message';

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [DecimalPipe, FormsModule, TranslatePipe,
    Button, InputText, InputNumber, TableModule, Tag, Card, TooltipModule, Message],
  templateUrl: './roles.component.html',
  styleUrl: './roles.component.scss',
})
export class RolesComponent implements OnInit {
  private readonly store      = inject(StaffStore);
  private readonly confirmSvc = inject(ConfirmService);
  private readonly toast      = inject(ToastService);

  @ViewChild('formAnchor', { read: ElementRef }) private formAnchor?: ElementRef<HTMLElement>;

  readonly roles   = this.store.roles;
  readonly loading = this.store.loading;

  showForm = signal(false);
  newRoleName = '';
  newRoleRate = 0;
  saving = signal(false);
  error  = signal('');

  // Inline rate editing state
  editingRateName  = signal<string | null>(null);
  editingRateValue = signal(0);
  savingRate       = signal(false);

  ngOnInit(): void { this.store.loadRoles(); }

  openAdd(): void {
    this.newRoleName = '';
    this.newRoleRate = 0;
    this.error.set('');
    this.showForm.set(true);
    setTimeout(() => this.formAnchor?.nativeElement?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  cancel(): void { this.showForm.set(false); this.error.set(''); }

  addRole(): void {
    const name = this.newRoleName.trim().toUpperCase();
    if (!name) return;
    this.saving.set(true);
    this.error.set('');
    this.store.createRole(name, this.newRoleRate).subscribe({
      next: () => {
        this.newRoleName = '';
        this.newRoleRate = 0;
        this.saving.set(false);
        this.showForm.set(false);
        this.toast.success('Role created.');
      },
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
      message: 'Deleting this role will remove it from all staff members. Are you sure?',
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
