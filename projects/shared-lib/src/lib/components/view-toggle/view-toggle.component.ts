import { Component, computed, inject, input } from '@angular/core';
import { ViewMode, ViewModeService } from '../../services/view-mode.service';

/**
 * The Cards / Table switch of a list screen, shown in the page toolbar. The screen passes its short key
 * (for example 'staff') and reads the same key from ViewModeService to decide what to render.
 */
@Component({
  selector: 'nc-view-toggle',
  standalone: true,
  template: `
    <div class="nc-view-toggle" role="group" aria-label="View">
      @for (o of options; track o.mode) {
        <button
          type="button"
          class="nc-view-btn"
          [class.on]="current() === o.mode"
          [attr.aria-pressed]="current() === o.mode"
          [attr.aria-label]="o.label"
          [title]="o.label"
          (click)="choose(o.mode)"
        >
          <i [class]="o.icon" aria-hidden="true"></i>
        </button>
      }
    </div>
  `,
  styles: `
    :host {
      display: inline-flex;
    }
    .nc-view-toggle {
      display: inline-flex;
      border: 1px solid var(--nc-border-subtle);
      border-radius: 8px;
      overflow: hidden;
      background: var(--nc-bg-surface);
    }
    .nc-view-btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.1rem;
      height: 2rem;
      border: 0;
      background: transparent;
      color: var(--nc-text-muted);
      cursor: pointer;
    }
    .nc-view-btn + .nc-view-btn {
      border-left: 1px solid var(--nc-border-subtle);
    }
    .nc-view-btn.on {
      background: var(--nc-bg-raised);
      color: var(--nc-text-primary);
    }
    .nc-view-btn:focus-visible {
      outline: 2px solid var(--nc-border-glow);
      outline-offset: -2px;
    }
  `,
})
export class ViewToggleComponent {
  private readonly views = inject(ViewModeService);

  /** The screen's short key, shared with the screen's own `ViewModeService.mode(key)` read. */
  readonly viewKey = input.required<string>();

  readonly options: { mode: ViewMode; label: string; icon: string }[] = [
    { mode: 'cards', label: 'Card view', icon: 'pi pi-th-large' },
    { mode: 'table', label: 'Table view', icon: 'pi pi-list' },
  ];

  readonly current = computed(() => this.views.mode(this.viewKey())());

  choose(mode: ViewMode): void {
    this.views.set(this.viewKey(), mode);
  }
}
