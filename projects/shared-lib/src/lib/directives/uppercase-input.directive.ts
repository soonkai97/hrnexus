import { Directive, ElementRef, HostListener, inject } from '@angular/core';
import { NgControl } from '@angular/forms';

/**
 * Forces a text input's value to UPPERCASE as the user types (and on paste).
 *
 * Staff IDs are stored uppercase (`StaffId.Trim().ToUpperInvariant()` on the server), so the login
 * and forgot-password fields uppercase the input for a consistent display — the backend matches
 * case-insensitively regardless, this is purely so what the user sees matches what is stored.
 *
 * Opt-in via the `ncUppercase` attribute. Works with both reactive forms (`formControlName`) and
 * template-driven forms (`[(ngModel)]`) by writing back through the bound NgControl; if there is no
 * control it falls back to mutating the native value. The caret position is preserved (uppercasing
 * never changes length, so the selection offsets stay valid).
 */
@Directive({
  selector: 'input[ncUppercase]',
  standalone: true,
})
export class UppercaseInputDirective {
  private readonly ngControl = inject(NgControl, { optional: true, self: true });
  private readonly el = inject(ElementRef<HTMLInputElement>);

  @HostListener('input')
  onInput(): void {
    const input = this.el.nativeElement;
    const value = input.value;
    const upper = value.toUpperCase();
    if (upper === value) return;

    const start = input.selectionStart;
    const end = input.selectionEnd;

    if (this.ngControl?.control) {
      this.ngControl.control.setValue(upper);
    } else {
      input.value = upper;
    }
    // Restore the caret — length is unchanged, so the offsets remain valid.
    if (start !== null && end !== null) input.setSelectionRange(start, end);
  }
}
