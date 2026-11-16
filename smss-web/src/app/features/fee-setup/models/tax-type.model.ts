// Mirrors TaxTypeDto / TaxTypeRequest.

export interface TaxType {
  taxTypeId: number;
  taxName: string;
  taxCode: string;
  ratePercent: number;
  description?: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Status is never sent: it changes only through activate / deactivate. */
export interface TaxTypeRequest {
  taxName: string;
  taxCode: string;
  ratePercent: number;
  description: string | null;
}

/** List table row: display-ready text. */
export interface TaxTypeRow extends Record<string, unknown> {
  taxTypeId: number;
  taxName: string;
  taxCode: string;
  rateLabel: string;
  description: string;
  statusName: string;
  isActive: boolean;
}

/** "5%", "12.5%", "18%". */
export const formatRate = (rate: number): string => `${rate.toLocaleString('en-IN', { maximumFractionDigits: 2 })}%`;

/** "GST (5%)" for dropdowns and summaries. */
export const taxOptionLabel = (t: Pick<TaxType, 'taxName' | 'ratePercent'>): string => `${t.taxName} (${formatRate(t.ratePercent)})`;

/**
 * Tax on a fee, in paise: ROUND_HALF_UP(fee x rate / 100, 2 decimals) - the same rule as the server (which is the authority; this only previews it).
 * Integer arithmetic on paise and basis points of a percent, so 0.30 x 5% is exactly 1.5 paise and rounds up, never a floating-point residue.
 */
export function taxPaise(feePaise: number, ratePercent: number): number {
  const rateHundredths = Math.round(ratePercent * 100); // 5 % -> 500, 12.5 % -> 1250
  const numerator = feePaise * rateHundredths; // paise x (percent x 100)
  return Math.floor((numerator + 5000) / 10000); // /10000 gives paise; +5000 rounds half up
}
