import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

/**
 * ThemeService — persists dark/light theme to localStorage and applies it to the DOM.
 *
 * WHY these tests matter:
 *  - ThemeService is the single source of truth for UI theming across both apps.
 *    A wrong initial value would render the wrong theme on every page load.
 *  - toggle() must alternate correctly so users can switch themes reliably.
 *  - The service must write the chosen theme to localStorage so it survives
 *    page reloads (critical for user preference persistence).
 *  - The `data-theme` attribute on `<html>` is what PrimeNG's dark-mode selector
 *    reads — if it's wrong, PrimeNG component colours break globally.
 */
describe('ThemeService', () => {
  /** Reset DOM and localStorage, configure TestBed, and return the service. */
  function setup(storedTheme?: 'dark' | 'light'): ThemeService {
    localStorage.removeItem('nc-theme');
    document.documentElement.removeAttribute('data-theme');
    document.body.classList.remove('nc-light');
    if (storedTheme) {
      localStorage.setItem('nc-theme', storedTheme);
    }
    TestBed.configureTestingModule({ providers: [ThemeService] });
    return TestBed.inject(ThemeService);
  }

  afterEach(() => {
    localStorage.removeItem('nc-theme');
    document.documentElement.removeAttribute('data-theme');
    document.body.classList.remove('nc-light');
    TestBed.resetTestingModule();
  });

  // ── Initial state ──────────────────────────────────────────────────────────

  describe('initial state', () => {
    it('defaults to "light" when no value is stored in localStorage', () => {
      const service = setup();
      expect(service.theme()).toBe('light');
    });

    it('restores the persisted theme from localStorage on construction', () => {
      const service = setup('dark');
      expect(service.theme()).toBe('dark');
    });

    it('applies data-theme="light" to <html> on construction when no stored value', () => {
      setup();
      expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    });

    it('adds nc-light class to <body> when theme is "light" (default)', () => {
      setup();
      expect(document.body.classList.contains('nc-light')).toBe(true);
    });

    it('does not add nc-light class to <body> when localStorage contains "dark"', () => {
      setup('dark');
      expect(document.body.classList.contains('nc-light')).toBe(false);
    });
  });

  // ── toggle() ──────────────────────────────────────────────────────────────

  describe('toggle()', () => {
    it('switches from "light" to "dark"', () => {
      const service = setup();
      expect(service.theme()).toBe('light');
      service.toggle();
      expect(service.theme()).toBe('dark');
    });

    it('switches from "dark" back to "light"', () => {
      const service = setup('dark');
      service.toggle();
      expect(service.theme()).toBe('light');
    });

    it('alternates theme on each successive toggle call', () => {
      const service = setup();
      service.toggle(); // light → dark
      service.toggle(); // dark → light
      service.toggle(); // light → dark
      expect(service.theme()).toBe('dark');
    });

    it('persists the toggled theme to localStorage', () => {
      const service = setup();
      service.toggle();
      // Run effects synchronously
      TestBed.flushEffects();
      expect(localStorage.getItem('nc-theme')).toBe('dark');
    });
  });
});
