export type RateScheme =
  'epf' | 'socso' | 'eis' | 'hrd-compulsory' | 'hrd-optional' | 'minimum-wage';
export type RateKind = 'percentage' | 'band-table';
export type EmployerRateMode = 'flat' | 'whole-wage-by-threshold';
export type RateRounding = 'none' | 'nearest-cent' | 'nearest-5-sen' | 'up-to-next-ringgit';

/** One wage band of a band-table rate (for display only; the server does the arithmetic). */
export interface StatutoryRateBand {
  wageFrom: number;
  wageTo: number;
  employeeAmount: number;
  employerAmount: number;
}

/** One statutory rate row. Rates are fractions (0.11 = 11%); money values are display-only numbers. */
export interface StatutoryRate {
  id: string;
  scheme: RateScheme;
  citizenshipClass: string;
  ageFrom: number;
  ageTo: number | null;
  effectiveFrom: string; // yyyy-MM-dd
  kind: RateKind;
  wageCeiling: number | null;
  employeeRate: number;
  employerRate: number;
  employerRateMode: EmployerRateMode;
  employerRateThreshold: number | null;
  employerRateAbove: number | null;
  rounding: RateRounding;
  amount: number | null;
  active: boolean;
  verified: boolean;
  verifiedBy: string | null;
  verifiedAt: string | null; // ISO timestamp
  verificationNote: string | null;
  source: string;
  note: string | null;
  bands: StatutoryRateBand[];
}
