import { Component, inject } from '@angular/core';
import { ThemeService } from '../../services/theme.service';
import { Button } from 'primeng/button';

@Component({
  selector: 'nc-theme-toggle',
  standalone: true,
  imports: [Button],
  templateUrl: './theme-toggle.component.html',
  styleUrl:    './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  readonly themeSvc = inject(ThemeService);
}
