import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

/**
 * Phase 7i guard: the product is English only. This fails the build if multi-language support
 * (ngx-translate, the language service, localized-name pipe, *Zh/*Vi fields) creeps back in.
 * Reintroducing a second language needs a new phase document and an ADR (docs/architecture).
 *
 * The forbidden identifiers are assembled from fragments so this file never matches itself.
 */
const FORBIDDEN: { label: string; pattern: RegExp }[] = [
  { label: 'ngx-translate', pattern: new RegExp('ngx' + '-translate') },
  { label: 'translate pipe', pattern: new RegExp('\\|\\s*trans' + 'late\\b') },
  {
    label: 'TranslatePipe/Service/Module',
    pattern: new RegExp('Trans' + 'late(Pipe|Service|Module)'),
  },
  { label: 'provideTranslateService', pattern: new RegExp('provideTrans' + 'lateService') },
  { label: 'LangService', pattern: new RegExp('Lang' + 'Service') },
  { label: 'LocalizedNamePipe', pattern: new RegExp('Localized' + 'NamePipe') },
  { label: 'localizedNameFor', pattern: new RegExp('localized' + 'NameFor') },
  { label: 'nameZh/nameVi', pattern: new RegExp('name' + '(Zh|Vi)', 'i') },
  { label: 'preferredLanguage', pattern: new RegExp('preferred' + 'Language', 'i') },
];

const SKIP_DIRS = new Set(['node_modules', 'dist', '.angular', 'coverage']);
const SCANNED = /\.(ts|html|scss|json)$/;

function walk(dir: string, out: string[]): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) walk(join(dir, entry.name), out);
    } else if (SCANNED.test(entry.name)) {
      out.push(join(dir, entry.name));
    }
  }
  return out;
}

describe('English only (Phase 7i guard)', () => {
  const root = resolve('.');
  const files = walk(join(root, 'projects'), []).filter(
    (f) => !f.endsWith('english-only.guard.spec.ts'),
  );

  it('finds source files to scan', () => {
    expect(files.length).toBeGreaterThan(100);
  });

  for (const { label, pattern } of FORBIDDEN) {
    it(`has no "${label}" anywhere under projects/`, () => {
      const hits = files
        .filter((f) => pattern.test(readFileSync(f, 'utf8')))
        .map((f) => f.replace(root, ''));
      expect(hits).toEqual([]);
    });
  }

  it('has no i18n locale folders', () => {
    const locales = files
      .filter((f) => /[\\/]public[\\/]i18n[\\/]/.test(f))
      .map((f) => f.replace(root, ''));
    expect(locales).toEqual([]);
  });

  it('declares no ngx-translate dependency in package.json', () => {
    const pkg = readFileSync(join(root, 'package.json'), 'utf8');
    expect(pkg).not.toMatch(new RegExp('@ngx' + '-translate'));
  });
});
