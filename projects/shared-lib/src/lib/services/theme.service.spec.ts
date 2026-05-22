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
    it('defaults to "dark" when no value is stored in localStorage', () => {
      const service = setup();
      expect(service.theme()).toBe('dark');
    });

    it('restores the persisted theme from localStorage on construction', () => {
      const service = setup('light');
      expect(service.theme()).toBe('light');
    });

    it('applies data-theme attribute to <html> on construction', () => {
      setup();
      expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    });

    it('adds nc-light class to <body> when localStorage contains "light"', () => {
      setup('light');
      expect(document.body.classList.contains('nc-light')).toBe(true);
    });

    it('does not add nc-light class to <body> when theme is "dark"', () => {
      setup();
      expect(document.body.classList.contains('nc-light')).toBe(false);
    });
  });

  // ── toggle() ──────────────────────────────────────────────────────────────

  describe('toggle()', () => {
    it('switches from "dark" to "light"', () => {
      const service = setup();
      expect(service.theme()).toBe('dark');
      service.toggle();
      expect(service.theme()).toBe('light');
    });

    it('switches from "light" back to "dark"', () => {
      const service = setup('light');
      service.toggle();
      expect(service.theme()).toBe('dark');
    });

    it('alternates theme on each successive toggle call', () => {
      const service = setup();
      service.toggle(); // dark → light
      service.toggle(); // light → dark
      service.toggle(); // dark → light
      expect(service.theme()).toBe('light');
    });

    it('persists the toggled theme to localStorage', () => {
      const service = setup();
      service.toggle();
      // Run effects synchronously
      TestBed.flushEffects();
      expect(localStorage.getItem('nc-theme')).toBe('light');
    });
  });
});
