import { Pipe, PipeTransform, inject } from '@angular/core';
import { LangService } from '../services/lang.service';

/**
 * Picks a record's display name for a given UI language:
 *  - zh → the Chinese name (nameZh) when set
 *  - vi → the Vietnamese name (nameVi) when set
 *  - otherwise (incl. en, or a blank translation) → the primary name
 *
 * Pure function so it can be reused by component computed() option arrays
 * (p-select / p-multiselect labels) as well as the pipe below.
 */
export function localizedNameFor(
  lang: string,
  name: string | null | undefined,
  nameZh?: string | null,
  nameVi?: string | null,
): string {
  const primary = name ?? '';
  if (lang === 'zh' && nameZh?.trim()) return nameZh.trim();
  if (lang === 'vi' && nameVi?.trim()) return nameVi.trim();
  return primary;
}

/**
 * Displays venue-entered data (locations, jobs) in the current UI language so a
 * name typed in Chinese/Vietnamese shows up for those users while everyone else
 * sees the primary (English/romanized) name.
 *
 * Impure so it re-evaluates when the language changes (like ngx-translate's pipe);
 * the lists it renders are small, so the cost is negligible.
 *
 * Usage: {{ loc.name | localizedName: loc.nameZh : loc.nameVi }}
 */
@Pipe({ name: 'localizedName', standalone: true, pure: false })
export class LocalizedNamePipe implements PipeTransform {
  private readonly lang = inject(LangService);

  transform(name: string | null | undefined, nameZh?: string | null, nameVi?: string | null): string {
    return localizedNameFor(this.lang.current(), name, nameZh, nameVi);
  }
}
