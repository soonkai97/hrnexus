import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { ThemeToggleComponent, SetPasswordBase } from 'shared-lib';

@Component({
  selector: 'app-set-password',
  standalone: true,
  imports: [FormsModule, ThemeToggleComponent, TranslatePipe],
  templateUrl: './set-password.component.html',
  styleUrl:    './set-password.component.scss',
})
export class SetPasswordComponent extends SetPasswordBase {
  protected override readonly redirectPath = '/dashboard';
}
