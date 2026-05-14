import { ENVIRONMENT_INITIALIZER, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';

/**
 * HOW THIS WORKS
 * --------------
 * `autocomplete="off"` is ignored by Chrome for any input that Chrome
 * recognises as a personal-data field (name, email, phone, address …).
 * Chrome's autofill DOES honour `readonly`, however, because it performs
 * a one-time proactive scan of the form when it is inserted into the DOM
 * and skips any input that is `readonly` at that moment.
 *
 * Strategy:
 *   1. As soon as Angular adds an input to the DOM (via MutationObserver),
 *      stamp it with `autocomplete="off"` and temporarily mark it `readonly`.
 *   2. Chrome scans, sees `readonly`, and excludes the input from its
 *      autofill candidates list for the lifetime of that DOM node.
 *   3. The very first user interaction (mousedown / touchstart / focus on
 *      capture phase) removes the temporary readonly so the user can type
 *      as normal — the autofill decision has already been made.
 *
 * `data-naf`    – marks inputs we have already processed (idempotent).
 * `data-naf-ro` – marks inputs where WE added readonly (so we only remove
 *                 readonly that we added, never readonly that was already there).
 */

const UNSTAMPED  = 'input:not([data-naf]),select:not([data-naf]),textarea:not([data-naf])';
const TEMP_RO    = 'data-naf-ro';

function stamp(el: HTMLElement): void {
  // Standard: all browsers use this as the first signal.
  el.setAttribute('autocomplete', 'off');
  // Edge / Microsoft Autofill: respects this attribute to skip a field.
  el.setAttribute('data-form-type', 'other');
  // LastPass, Bitwarden, Dashlane and similar browser extensions.
  el.setAttribute('data-lpignore', 'true');
  el.setAttribute('data-bwignore', 'true');
  // Mark as processed so we never double-stamp.
  el.setAttribute('data-naf', '');

  // Readonly trick: Chrome/Edge/Firefox/Safari all skip readonly inputs
  // during their proactive autofill scan when a form enters the DOM.
  // We remove this readonly the instant the user first touches the field.
  // <select> does not support readonly; also skip already-readonly/disabled inputs.
  if (
    el.tagName !== 'SELECT' &&
    !el.hasAttribute('readonly') &&
    !el.hasAttribute('disabled')
  ) {
    el.setAttribute('readonly', '');
    el.setAttribute(TEMP_RO, '');
  }
}

function stampAll(root: Element | Document): void {
  root.querySelectorAll<HTMLElement>(UNSTAMPED).forEach(stamp);
}

/** Fired on the first real user interaction — removes our temporary readonly. */
function enableInput(e: Event): void {
  const el = e.target as HTMLElement | null;
  if (el?.hasAttribute?.(TEMP_RO)) {
    el.removeAttribute('readonly');
    el.removeAttribute(TEMP_RO);
  }
}

/**
 * Add to the `providers` array in both `app.config.ts` files.
 * No per-component imports or per-input HTML attributes needed.
 */
export function provideNoAutofill() {
  return {
    provide: ENVIRONMENT_INITIALIZER,
    multi: true,
    useValue: () => {
      const doc = inject(DOCUMENT);

      // Stamp anything already in the DOM at boot (login page inputs etc.).
      stampAll(doc);

      // Watch for inputs added later by Angular's lazy router / @if blocks.
      new MutationObserver(mutations => {
        for (const m of mutations) {
          for (const node of Array.from(m.addedNodes)) {
            if (!(node instanceof Element)) continue;
            if ((node as HTMLElement).matches?.(UNSTAMPED)) {
              stamp(node as HTMLElement);
            }
            stampAll(node as Element);
          }
        }
      }).observe(doc.body, { childList: true, subtree: true });

      // Remove temporary readonly the instant the user reaches the input.
      // Capture phase → runs before Angular's own event handlers.
      doc.addEventListener('mousedown',  enableInput, true);
      doc.addEventListener('touchstart', enableInput, { capture: true, passive: true });
      doc.addEventListener('focus',      enableInput, true);
    },
  };
}
