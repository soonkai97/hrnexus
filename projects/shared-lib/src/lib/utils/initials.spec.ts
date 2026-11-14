import { initials } from './initials';

/** WHY: the avatar on every person card is built from this, so odd names (one word, extra spaces, none) must not break or show "undefined". */
describe('initials', () => {
  it('uses the first letter of the first and last word', () => {
    expect(initials('Anh Nguyen')).toBe('AN');
    expect(initials('Mei Ling Tran')).toBe('MT');
  });

  it('upper-cases and ignores extra spaces', () => {
    expect(initials('  sam   ho ')).toBe('SH');
  });

  it('gives one letter for a single word', () => {
    expect(initials('Cher')).toBe('C');
  });

  it('gives a question mark when there is no name', () => {
    expect(initials('')).toBe('?');
    expect(initials('   ')).toBe('?');
    expect(initials(null)).toBe('?');
    expect(initials(undefined)).toBe('?');
  });
});
