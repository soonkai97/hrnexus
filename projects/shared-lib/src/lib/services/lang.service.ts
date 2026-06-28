import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TranslateService } from '@ngx-translate/core';
import { MenuItem } from 'primeng/api';

const STORAGE_KEY = 'nc_lang';
const SUPPORTED = ['en', 'zh', 'vi'] as const;
type Lang = typeof SUPPORTED[number];

/** Short label shown on the globe button, and native name shown in the dropdown. */
const LANG_LABELS: Record<Lang, { short: string; native: string }> = {
  en: { short: 'EN',   native: 'English' },
  zh: { short: '中文', native: '中文' },
  vi: { short: 'VI',   native: 'Tiếng Việt' },
};

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

  /** Advance to the next supported language, cycling en → zh → vi → en. */
  toggle(): void {
    const idx = SUPPORTED.indexOf(this.current());
    const next = SUPPORTED[(idx + 1) % SUPPORTED.length]!;
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

  /** Short label for the current language, for the globe button (EN / 中文 / VI). */
  label(): string {
    return LANG_LABELS[this.current()].short;
  }

  /**
   * Dropdown items for a language menu — native labels, a check mark on the active one.
   * Called from templates, so it re-evaluates on change detection and reflects current().
   */
  menuItems(): MenuItem[] {
    const cur = this.current();
    return SUPPORTED.map(l => ({
      label: LANG_LABELS[l].native,
      icon: cur === l ? 'pi pi-check' : 'pi pi-globe',
      command: () => this.set(l),
    }));
  }

  private getStored(): Lang {
    const stored = localStorage.getItem(STORAGE_KEY);
    return (SUPPORTED as readonly string[]).includes(stored ?? '') ? (stored as Lang) : 'en';
  }
}
