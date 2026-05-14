import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ThemeService } from '../../services/theme.service';

@Component({
  selector: 'nc-theme-toggle',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './theme-toggle.component.html',
  styleUrl:    './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  readonly themeSvc = inject(ThemeService);
}
