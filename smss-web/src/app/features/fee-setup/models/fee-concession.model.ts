// Mirrors FeeConcessionResponse / Create+UpdateFeeConcessionRequest.

export type ConcessionType = 'PERCENTAGE' | 'FIXED';

export interface FeeConcession {
  concessionId: number;
  schoolId: string;
  concessionName: string;
  concessionCode: string;
  concessionType: ConcessionType;
  value: number;
  /** Null = applies to every fee head of the structure. */
  applicableFeeTypeId?: number | null;
  applicableFeeTypeName?: string | null;
  isActive: boolean;
  /** "Active" / "Inactive". */
  statusName: string;
  createdAt: string;
  updatedAt: string;
}

/** Status is never sent: it changes only through toggle-status. */
export interface FeeConcessionRequest {
  concessionName: string;
  concessionCode: string;
  concessionType: ConcessionType;
  value: number;
  applicableFeeTypeId: number | null;
}

/** List table row: display-ready text. */
export interface FeeConcessionRow extends Record<string, unknown> {
  concessionId: number;
  concessionName: string;
  concessionCode: string;
  typeLabel: string;
  valueLabel: string;
  appliesTo: string;
  statusName: string;
  isActive: boolean;
}

const inr = (n: number) => '₹' + n.toLocaleString('en-IN', { minimumFractionDigits: 0, maximumFractionDigits: 2 });

/**
 * A concession's name with its value: "Sibling (10%)" or "Staff ward (₹500)". When the NAME already states the value ("Sibling 10%", "Staff Rs 500") it is not repeated.
 * A percentage counts as stated when the name holds that number followed by "%"; a fixed amount when the name holds that number at all.
 */
export function concessionNameWithValue(name: string, type: ConcessionType, value: number): string {
  const pattern = type === 'PERCENTAGE' ? /(\d+(?:\.\d+)?)\s*%/g : /(\d+(?:\.\d+)?)/g;
  const stated = [...name.matchAll(pattern)].some((m) => Number(m[1]) === value);
  return stated ? name : `${name} (${formatConcessionValue(type, value)})`;
}

/**
 * What a concession takes off, in paise, from `basePaise`: the sum of the allocations it applies to. PERCENTAGE rounds half up on exact integer arithmetic (the server
 * does the same in decimal); FIXED is the amount, once. Never more than the base.
 */
export function concessionPaise(type: ConcessionType, value: number, basePaise: number): number {
  if (basePaise <= 0 || value <= 0) return 0;
  const d = type === 'PERCENTAGE' ? Math.floor((basePaise * Math.round(value * 100) + 5000) / 10000) : Math.round(value * 100);
  return Math.min(d, basePaise);
}

/** "15%" or "₹2,000". */
export function formatConcessionValue(type: ConcessionType, value: number): string {
  return type === 'PERCENTAGE' ? `${value.toLocaleString('en-IN', { maximumFractionDigits: 2 })}%` : inr(value);
}
