import { Component, inject } from '@angular/core';
import { ThemeService } from '../../services/theme.service';

@Component({
  selector: 'nc-theme-toggle',
  standalone: true,
  imports: [],
  templateUrl: './theme-toggle.component.html',
  styleUrl:    './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  readonly themeSvc = inject(ThemeService);
}
