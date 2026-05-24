import { Injectable, signal, effect } from '@angular/core';

export type Theme = 'dark' | 'light';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>((localStorage.getItem('nc-theme') as Theme) ?? 'light');

  constructor() {
    effect(() => {
      const t = this.theme();
      document.documentElement.setAttribute('data-theme', t);
      localStorage.setItem('nc-theme', t);
      if (t === 'light') {
        document.body.classList.add('nc-light');
      } else {
        document.body.classList.remove('nc-light');
      }
    });
    // Apply immediately on init
    document.documentElement.setAttribute('data-theme', this.theme());
    if (this.theme() === 'light') {
      document.body.classList.add('nc-light');
    }
  }

  toggle(): void {
    this.theme.update(t => t === 'dark' ? 'light' : 'dark');
  }
}
