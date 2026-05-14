import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';

export interface ConfirmOptions { title?: string; message: string; confirmLabel?: string; danger?: boolean; }

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly visible   = signal(false);
  readonly options   = signal<ConfirmOptions>({ message: '' });
  private _result$   = new Subject<boolean>();

  confirm(opts: ConfirmOptions) {
    this.options.set(opts);
    this.visible.set(true);
    this._result$ = new Subject<boolean>(); // fresh subject per call — no stale subscribers
    return this._result$.asObservable();
  }

  resolve(result: boolean): void {
    this.visible.set(false);
    this._result$.next(result);
    this._result$.complete(); // auto-unsubscribes all listeners
  }
}
