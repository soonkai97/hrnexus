import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';

const STORAGE_KEY = 'nc_lang';
const SUPPORTED = ['en', 'zh'] as const;
type Lang = typeof SUPPORTED[number];

@Injectable({ providedIn: 'root' })
export class LangService {
  private readonly translate = inject(TranslateService);
  private readonly http      = inject(HttpClient);

  readonly current = signal<Lang>(this.getStored());

  constructor() {
    this.translate.addLangs([...SUPPORTED]);
    this.translate.setDefaultLang('en');
    this.translate.use(this.current());
  }

  toggle(): void {
    const next: Lang = this.current() === 'en' ? 'zh' : 'en';
    this.set(next);
  }

  /** Change language — updates UI instantly, persists to localStorage and backend. */
  set(lang: Lang, syncToApi = true): void {
    this.translate.use(lang);
    this.current.set(lang);
    localStorage.setItem(STORAGE_KEY, lang);
    if (syncToApi) {
      this.http.patch('/api/auth/language', { language: lang })
        .subscribe({ error: () => {} }); // fire-and-forget
    }
  }

  /**
   * Called by the shell after login — fetches preferred language from the backend
   * and applies it if it differs from the current value (e.g. first login on a new device).
   */
  syncFromApi(): void {
    this.http.get<{ language: string }>('/api/auth/language').subscribe({
      next: res => {
        if ((SUPPORTED as readonly string[]).includes(res.language as Lang)) {
          this.set(res.language as Lang, false); // apply without re-saving to API
        }
      },
      error: () => {},
    });
  }

  private getStored(): Lang {
    const stored = localStorage.getItem(STORAGE_KEY);
    return (SUPPORTED as readonly string[]).includes(stored ?? '') ? (stored as Lang) : 'en';
  }
}
