import { Component, inject } from '@angular/core';
import { ThemeService } from '../../services/theme.service';
import { Button } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';

@Component({
  selector: 'nc-theme-toggle',
  standalone: true,
  imports: [Button, TooltipModule],
  templateUrl: './theme-toggle.component.html',
  styleUrl:    './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  readonly themeSvc = inject(ThemeService);
}
