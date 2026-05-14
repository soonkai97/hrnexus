import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChangePasswordComponent } from 'shared-lib';

@Component({
  selector: 'app-change-password-page',
  standalone: true,
  imports: [RouterLink, ChangePasswordComponent],
  templateUrl: './change-password-page.component.html',
})
export class ChangePasswordPageComponent {}
