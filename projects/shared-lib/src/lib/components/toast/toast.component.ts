import { Component, ChangeDetectionStrategy } from '@angular/core';
import { Toast } from 'primeng/toast';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'nc-toast',
  standalone: true,
  imports: [Toast],
  template: `<p-toast position="top-center" [life]="4000" styleClass="app-toast" />`,
})
export class ToastComponent {}
