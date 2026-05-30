import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ChangePasswordComponent } from 'shared-lib';
import { Button } from 'primeng/button';
import { Card } from 'primeng/card';

@Component({
  selector: 'app-change-password-page',
  standalone: true,
  imports: [RouterLink, TranslatePipe, ChangePasswordComponent, Button, Card],
  templateUrl: './change-password-page.component.html',
})
export class ChangePasswordPageComponent {}
