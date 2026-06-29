import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LocalizedNamePipe } from './localized-name.pipe';
import { LangService } from '../services/lang.service';

/**
 * LocalizedNamePipe — shows venue-entered names (locations, jobs) in the UI language.
 *
 * WHY these tests matter:
 *  - A name typed in Chinese must appear for Chinese users, while English/romanized
 *    users keep seeing the primary name — so the SAME record reads correctly to both.
 *  - The Chinese name is OPTIONAL: when blank, every language must fall back to the
 *    primary name rather than render an empty cell.
 *  - Only the Chinese (`zh`) locale swaps in `nameZh`; other languages (en, vi) have no
 *    translated field and must show the primary name.
 */
describe('LocalizedNamePipe', () => {
  let pipe: LocalizedNamePipe;
  let lang: LangService;

  beforeEach(() => {
    localStorage.removeItem('nc_lang');
    TestBed.configureTestingModule({
      providers: [
        LocalizedNamePipe,
        LangService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService({ defaultLanguage: 'en' }),
      ],
    });
    pipe = TestBed.inject(LocalizedNamePipe);
    lang = TestBed.inject(LangService);
  });

  describe('Chinese language with a Chinese name set', () => {
    beforeEach(() => lang.set('zh', false));

    it('should show the Chinese name so zh users read the venue data in their language', () => {
      expect(pipe.transform('MAIN BAR', '主吧台')).toBe('主吧台');
    });

    it('should trim surrounding whitespace from the Chinese name', () => {
      expect(pipe.transform('MAIN BAR', '  主吧台  ')).toBe('主吧台');
    });

    it('should fall back to the primary name when no Chinese name is set', () => {
      expect(pipe.transform('MAIN BAR', null)).toBe('MAIN BAR');
    });

    it('should fall back to the primary name when the Chinese name is blank/whitespace', () => {
      expect(pipe.transform('MAIN BAR', '   ')).toBe('MAIN BAR');
    });
  });

  describe('non-Chinese languages always show the primary name', () => {
    it('should show the primary name in English even when a Chinese name exists', () => {
      lang.set('en', false);
      expect(pipe.transform('MAIN BAR', '主吧台')).toBe('MAIN BAR');
    });

    it('should show the primary name in Vietnamese even when a Chinese name exists', () => {
      lang.set('vi', false);
      expect(pipe.transform('MAIN BAR', '主吧台')).toBe('MAIN BAR');
    });
  });

  describe('null / empty guard', () => {
    it('should return an empty string when the primary name is null', () => {
      lang.set('en', false);
      expect(pipe.transform(null)).toBe('');
    });

    it('should return an empty string when the primary name is undefined', () => {
      lang.set('zh', false);
      expect(pipe.transform(undefined, null)).toBe('');
    });
  });
});
