export interface Location {
  id: string;
  name: string;
  /** Optional Chinese name; shown instead of `name` when the UI language is Chinese. */
  nameZh?: string | null;
  /** Optional Vietnamese name; shown instead of `name` when the UI language is Vietnamese. */
  nameVi?: string | null;
  imagePath: string | null;
}
