import { Component } from '@angular/core';
import { Toast } from 'primeng/toast';

@Component({
  selector: 'nc-toast',
  standalone: true,
  imports: [Toast],
  template: `<p-toast position="top-right" />`,
})
export class ToastComponent {}
