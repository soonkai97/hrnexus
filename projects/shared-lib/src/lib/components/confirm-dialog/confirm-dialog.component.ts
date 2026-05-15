import { Component } from '@angular/core';
import { ConfirmDialog } from 'primeng/confirmdialog';

@Component({
  selector: 'nc-confirm-dialog',
  standalone: true,
  imports: [ConfirmDialog],
  template: `<p-confirmDialog />`,
})
export class ConfirmDialogComponent {}
