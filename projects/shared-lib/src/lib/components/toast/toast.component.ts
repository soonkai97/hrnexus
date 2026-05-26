import { Component } from '@angular/core';
import { Toast } from 'primeng/toast';

@Component({
  selector: 'nc-toast',
  standalone: true,
  imports: [Toast],
  template: `<p-toast position="bottom-center" [life]="4000" styleClass="app-toast" />`,
})
export class ToastComponent {}
