import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService, Toast } from '../../services/toast.service';

@Component({
  selector: 'nc-toast',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './toast.component.html',
  styleUrl:    './toast.component.scss',
})
export class ToastComponent {
  readonly svc = inject(ToastService);

  icon(type: Toast['type']): string {
    return { success: '✓', error: '✗', warning: '⚠', info: 'ℹ' }[type];
  }
}
