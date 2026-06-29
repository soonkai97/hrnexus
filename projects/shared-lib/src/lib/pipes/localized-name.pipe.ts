import { Pipe, PipeTransform, inject } from '@angular/core';
import { LangService } from '../services/lang.service';

/**
 * Displays a record's name in the current UI language.
 * When the language is Chinese (`zh`) and an optional Chinese name (`nameZh`)
 * is set, returns it; otherwise falls back to the primary `name`.
 *
 * Used for venue-entered data (locations, jobs) so a name typed in Chinese
 * shows up for Chinese users while English/romanized users see the primary name.
 *
 * Impure so it re-evaluates when the language changes (like ngx-translate's pipe);
 * the lists it renders are small, so the cost is negligible.
 *
 * Usage: {{ loc.name | localizedName: loc.nameZh }}
 */
@Pipe({ name: 'localizedName', standalone: true, pure: false })
export class LocalizedNamePipe implements PipeTransform {
  private readonly lang = inject(LangService);

  transform(name: string | null | undefined, nameZh?: string | null): string {
    const primary = name ?? '';
    if (this.lang.current() === 'zh' && nameZh && nameZh.trim()) return nameZh.trim();
    return primary;
  }
}
