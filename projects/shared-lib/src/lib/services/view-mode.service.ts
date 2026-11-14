import { Injectable, Signal, WritableSignal, signal } from '@angular/core';

/** How a list screen shows its records: a grid of cards (the default) or a table. */
export type ViewMode = 'cards' | 'table';

const MODES: readonly ViewMode[] = ['cards', 'table'];
const KEY_PREFIX = 'view-mode:';

/**
 * The Cards or Table choice of each list screen, remembered per screen on this device. A screen asks for its
 * mode by a short key (for example 'staff'); every part of that screen then reads the same signal. Storage is
 * only a convenience: when the browser blocks it the choice still works for the current visit.
 */
@Injectable({ providedIn: 'root' })
export class ViewModeService {
  private readonly modes = new Map<string, WritableSignal<ViewMode>>();

  /** The current mode of a screen (cards until someone chooses the table). */
  mode(key: string): Signal<ViewMode> {
    return this.signalFor(key).asReadonly();
  }

  /** Change a screen's mode and remember it for next time. */
  set(key: string, mode: ViewMode): void {
    this.signalFor(key).set(mode);
    try {
      localStorage.setItem(KEY_PREFIX + key, mode);
    } catch {
      /* storage blocked: the choice lasts for this visit only */
    }
  }

  private signalFor(key: string): WritableSignal<ViewMode> {
    let s = this.modes.get(key);
    if (!s) {
      s = signal<ViewMode>(this.read(key));
      this.modes.set(key, s);
    }
    return s;
  }

  private read(key: string): ViewMode {
    try {
      const stored = localStorage.getItem(KEY_PREFIX + key);
      return MODES.find((m) => m === stored) ?? 'cards';
    } catch {
      return 'cards';
    }
  }
}
