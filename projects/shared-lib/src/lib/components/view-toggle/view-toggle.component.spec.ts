import { TestBed } from '@angular/core/testing';
import { ViewModeService } from '../../services/view-mode.service';
import { ViewToggleComponent } from './view-toggle.component';

/**
 * WHY these tests matter: the toggle is the only way to switch between the list and the cards, and it must
 * drive the same choice the screen reads. A toggle that updates its own copy would leave the screen on the
 * wrong view.
 */
describe('ViewToggleComponent', () => {
  function make(key: string) {
    const fixture = TestBed.createComponent(ViewToggleComponent);
    fixture.componentRef.setInput('viewKey', key);
    fixture.detectChanges();
    return fixture;
  }

  const buttons = (el: HTMLElement) => [...el.querySelectorAll('button')] as HTMLButtonElement[];

  beforeEach(() => {
    vi.stubGlobal('localStorage', { getItem: () => null, setItem: () => undefined });
    TestBed.configureTestingModule({ imports: [ViewToggleComponent] });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('offers a card view and a table view, each with an accessible name', () => {
    expect(buttons(make('staff').nativeElement).map((b) => b.getAttribute('aria-label'))).toEqual([
      'Card view',
      'Table view',
    ]);
  });

  it("shows the screen's current view as pressed (the list until cards are chosen)", () => {
    const [cards, table] = buttons(make('staff').nativeElement);

    expect(table?.getAttribute('aria-pressed')).toBe('true');
    expect(cards?.getAttribute('aria-pressed')).toBe('false');
  });

  it('choosing cards changes the choice the screen reads', () => {
    const fixture = make('staff');
    const views = TestBed.inject(ViewModeService);

    buttons(fixture.nativeElement)[0]!.click();
    fixture.detectChanges();

    expect(views.mode('staff')()).toBe('cards');
    expect(buttons(fixture.nativeElement)[0]!.getAttribute('aria-pressed')).toBe('true');
  });

  it('only changes the screen it was given the key of', () => {
    const fixture = make('roles');
    const views = TestBed.inject(ViewModeService);

    buttons(fixture.nativeElement)[0]!.click();

    expect(views.mode('roles')()).toBe('cards');
    expect(views.mode('staff')()).toBe('table');
  });
});
