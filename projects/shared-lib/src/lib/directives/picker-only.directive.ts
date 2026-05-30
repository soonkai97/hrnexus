import { Directive, ElementRef, HostListener, inject, OnInit } from '@angular/core';

/**
 * Automatically applied to every <input type="date"> and <input type="time">.
 * - Sets inputmode="none" so mobile OSes never show the software keyboard.
 * - Clicking anywhere on the input immediately opens the native picker.
 * - Keyboard input is blocked to prevent invalid values being typed manually.
 */
@Directive({
  // eslint-disable-next-line @angular-eslint/directive-selector -- intentionally targets native date/time inputs, not a prefixed attribute
  selector: 'input[type=date], input[type=time]',
  standalone: true,
})
export class PickerOnlyDirective implements OnInit {
  private readonly el = inject(ElementRef<HTMLInputElement>);

  ngOnInit(): void {
    // Suppress the mobile soft keyboard — the native date/time picker handles input
    this.el.nativeElement.setAttribute('inputmode', 'none');
  }

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
