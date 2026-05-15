import { Injectable, inject } from '@angular/core';
import { MessageService } from 'primeng/api';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly msg = inject(MessageService);

  show(message: string, type: ToastType = 'info', durationMs = 4000): void {
    const severity = type === 'warning' ? 'warn' : type;
    this.msg.add({ severity, detail: message, life: durationMs });
  }

  success(msg: string) { this.show(msg, 'success'); }
  error(msg: string)   { this.show(msg, 'error', 5000); }
  warning(msg: string) { this.show(msg, 'warning'); }
}
