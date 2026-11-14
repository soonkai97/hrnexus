/**
 * The initials shown in a card's avatar: the first letter of the first and last word of a name, upper-cased.
 * One word gives one letter; an empty or missing name gives "?".
 */
export function initials(name: string | null | undefined): string {
  const words = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  const first = words[0]!.charAt(0);
  const last = words.length > 1 ? words[words.length - 1]!.charAt(0) : '';
  return (first + last).toUpperCase();
}
