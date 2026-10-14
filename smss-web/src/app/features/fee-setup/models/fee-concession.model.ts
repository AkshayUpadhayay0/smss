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

/** "15%" or "₹2,000". */
export function formatConcessionValue(type: ConcessionType, value: number): string {
  return type === 'PERCENTAGE' ? `${value.toLocaleString('en-IN', { maximumFractionDigits: 2 })}%` : inr(value);
}
