import { Directive, ElementRef, HostListener, inject } from '@angular/core';

/**
 * Automatically applied to every <input type="date"> and <input type="time">.
 * - Clicking anywhere on the input immediately opens the native picker.
 * - Keyboard input is blocked to prevent invalid values being typed manually.
 */
@Directive({
  selector: 'input[type=date], input[type=time]',
  standalone: true,
})
export class PickerOnlyDirective {
  private readonly el = inject(ElementRef<HTMLInputElement>);

  @HostListener('click')
  onClick(): void {
    try {
      this.el.nativeElement.showPicker();
    } catch {
      // showPicker() may throw if the element is not visible / not user-activated;
      // silently ignore — the browser will still open the picker on its own.
    }
  }

  @HostListener('keydown', ['$event'])
  onKeyDown(e: KeyboardEvent): void {
    // Allow Tab and Escape for accessibility / form navigation
    if (e.key === 'Tab' || e.key === 'Escape') return;
    e.preventDefault();
  }
}
