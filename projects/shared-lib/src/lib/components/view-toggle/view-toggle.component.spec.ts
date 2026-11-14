import { TestBed } from '@angular/core/testing';
import { ViewModeService } from '../../services/view-mode.service';
import { ViewToggleComponent } from './view-toggle.component';

/**
 * WHY these tests matter: the toggle is the only way to reach the table view, and it must drive the same
 * choice the screen reads. A toggle that updates its own copy would leave the screen on the wrong view.
 */
describe('ViewToggleComponent', () => {
  function make(key: string) {
    const fixture = TestBed.createComponent(ViewToggleComponent);
    fixture.componentRef.setInput('viewKey', key);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    vi.stubGlobal('localStorage', { getItem: () => null, setItem: () => undefined });
    TestBed.configureTestingModule({ imports: [ViewToggleComponent] });
  });

  afterEach(() => vi.unstubAllGlobals());

  it('offers a card view and a table view, each with an accessible name', () => {
    const buttons = make('staff').nativeElement.querySelectorAll(
      'button',
    ) as NodeListOf<HTMLButtonElement>;

    expect([...buttons].map((b) => b.getAttribute('aria-label'))).toEqual([
      'Card view',
      'Table view',
    ]);
  });

  it("shows the screen's current view as pressed", () => {
    const el = make('staff').nativeElement as HTMLElement;
    const [cards, table] = [...el.querySelectorAll('button')];

    expect(cards?.getAttribute('aria-pressed')).toBe('true');
    expect(table?.getAttribute('aria-pressed')).toBe('false');
  });

  it('choosing the table changes the choice the screen reads', () => {
    const fixture = make('staff');
    const views = TestBed.inject(ViewModeService);

    (fixture.nativeElement.querySelectorAll('button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(views.mode('staff')()).toBe('table');
    expect(
      (fixture.nativeElement.querySelectorAll('button')[1] as HTMLButtonElement).getAttribute(
        'aria-pressed',
      ),
    ).toBe('true');
  });

  it('only changes the screen it was given the key of', () => {
    const fixture = make('roles');
    const views = TestBed.inject(ViewModeService);

    (fixture.nativeElement.querySelectorAll('button')[1] as HTMLButtonElement).click();

    expect(views.mode('roles')()).toBe('table');
    expect(views.mode('staff')()).toBe('cards');
  });
});
