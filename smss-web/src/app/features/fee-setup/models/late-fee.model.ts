// Mirrors LateFeeRuleDto / SaveLateFeeRuleRequest / LateFeePreviewDto. Nothing here ever carries a late fee AMOUNT to the server: it works that out itself.

export type LateFeeRuleType = 'FLAT' | 'PER_DAY' | 'PERCENT';

export const LATE_FEE_RULE_LABELS: Record<LateFeeRuleType, string> = {
  FLAT: 'A flat amount for each late installment',
  PER_DAY: 'An amount for each day late (after the grace days)',
  PERCENT: 'A percentage of the amount paid against the installment',
};

export interface LateFeeType {
  feeTypeId: number;
  name: string;
  isActive: boolean;
  /** Only fee types with this on are charged a late fee. */
  appliesLateFee: boolean;
}

export interface LateFeeRule {
  enabled: boolean;
  ruleType: LateFeeRuleType | null;
  amount: number | null;
  percent: number | null;
  graceDays: number;
  capPerDue: number | null;
  taxApplies: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
  feeTypes: LateFeeType[];
}

export interface SaveLateFeeRuleRequest {
  enabled: boolean;
  ruleType: LateFeeRuleType | null;
  amount: number | null;
  percent: number | null;
  graceDays: number;
  capPerDue: number | null;
  taxApplies: boolean;
  feeTypeIds: number[];
}
