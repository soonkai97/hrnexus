import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { ThemeToggleComponent, SetPasswordBase } from 'shared-lib';

@Component({
  selector: 'app-set-password',
  standalone: true,
  imports: [FormsModule, ThemeToggleComponent, TranslatePipe, Button, InputText, Message],
  templateUrl: './set-password.component.html',
  styleUrl:    './set-password.component.scss',
})
export class SetPasswordComponent extends SetPasswordBase {
  protected override readonly redirectPath = '/home';
}
