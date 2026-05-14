import {
  Directive, ElementRef, Input, OnDestroy,
  HostListener, NgZone
} from '@angular/core';

/**
 * Custom tooltip directive. Usage:
 *   <button [ncTooltip]="'Delete record'">🗑</button>
 *   <button ncTooltip="Edit">✏</button>
 *
 * The tooltip is appended to <body> and positions itself above the host
 * element (flips below if there isn't enough space). Styled via the global
 * `.nc-tooltip` class defined in each app's styles.scss.
 */
@Directive({
  selector: '[ncTooltip]',
  standalone: true,
})
export class TooltipDirective implements OnDestroy {
  @Input('ncTooltip') text = '';

  private tipEl: HTMLElement | null = null;
  private rafId = 0;

  constructor(private host: ElementRef<HTMLElement>, private zone: NgZone) {}

  @HostListener('mouseenter')
  onEnter(): void {
    if (!this.text?.trim()) return;
    this.zone.runOutsideAngular(() => {
      this.create();
    });
  }

  @HostListener('mouseleave')
  @HostListener('click')
  onLeave(): void {
    this.destroy();
  }

  private create(): void {
    this.destroy();
    const el = document.createElement('div');
    el.className = 'nc-tooltip';
    el.textContent = this.text;
    el.setAttribute('role', 'tooltip');
    // Start invisible so we can measure size before positioning
    el.style.opacity = '0';
    el.style.pointerEvents = 'none';
    document.body.appendChild(el);
    this.tipEl = el;

    // Position after paint so dimensions are available
    this.rafId = requestAnimationFrame(() => {
      this.position();
      if (this.tipEl) this.tipEl.style.opacity = '1';
    });
  }

  private position(): void {
    if (!this.tipEl) return;
    const host = this.host.nativeElement.getBoundingClientRect();
    const tip  = this.tipEl.getBoundingClientRect();
    const gap  = 6;
    const vw   = window.innerWidth;

    // Prefer above; flip below if not enough room
    let top = host.top - tip.height - gap;
    if (top < 8) top = host.bottom + gap;

    // Centre horizontally, clamp to viewport edges
    let left = host.left + host.width / 2 - tip.width / 2;
    left = Math.max(8, Math.min(left, vw - tip.width - 8));

    this.tipEl.style.top  = `${top}px`;
    this.tipEl.style.left = `${left}px`;
  }

  private destroy(): void {
    cancelAnimationFrame(this.rafId);
    this.tipEl?.remove();
    this.tipEl = null;
  }

  ngOnDestroy(): void {
    this.destroy();
  }
}
