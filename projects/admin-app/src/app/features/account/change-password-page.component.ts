import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ChangePasswordComponent } from 'shared-lib';
import { Card } from 'primeng/card';
import { Button } from 'primeng/button';

@Component({
  selector: 'app-change-password-page',
  standalone: true,
  imports: [RouterLink, ChangePasswordComponent, TranslatePipe, Card, Button],
  templateUrl: './change-password-page.component.html',
})
export class ChangePasswordPageComponent {}
