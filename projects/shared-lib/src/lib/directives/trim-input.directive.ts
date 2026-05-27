import { Directive, HostListener, inject } from '@angular/core';
import { NgModel } from '@angular/forms';

/**
 * Auto-trims whitespace from text inputs and textareas on blur.
 * Applied automatically to every pInputText / pTextarea with an ngModel binding
 * (excluding password, date, time, and number inputs).
 */
@Directive({
  // eslint-disable-next-line @angular-eslint/directive-selector
  selector: `
    input[pInputText][ngModel]:not([type=password]):not([type=date]):not([type=time]):not([type=number]),
    textarea[pTextarea][ngModel]
  `,
  standalone: true,
})
export class TrimInputDirective {
  private readonly ngModel = inject(NgModel, { optional: true, self: true });

  @HostListener('blur')
  onBlur(): void {
    const val = this.ngModel?.value;
    if (typeof val === 'string' && val !== val.trim()) {
      this.ngModel!.control.setValue(val.trim());
    }
  }
}
