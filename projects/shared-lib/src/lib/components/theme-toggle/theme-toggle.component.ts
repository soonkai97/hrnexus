import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { ThemeService } from '../../services/theme.service';
import { TranslatePipe } from '@ngx-translate/core';
import { Button } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'nc-theme-toggle',
  standalone: true,
  imports: [TranslatePipe, Button, TooltipModule],
  templateUrl: './theme-toggle.component.html',
  styleUrl:    './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  readonly themeSvc = inject(ThemeService);
}
