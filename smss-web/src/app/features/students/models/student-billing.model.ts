// Mirrors StudentFeeItemsDto / StudentFeeDuesDto / FeePreviewDto and the admission-time FeeItemSelection.

/** How a billing cycle is split into installments (NONE | QUARTERLY | MONTHLY), as allowed for one fee line. */
export interface InstallmentPlanOption {
  code: string;
  name: string;
  /** Installments in each billing cycle (None = 1). */
  installmentsPerCycle: number;
  /** Installments in a full academic year under this plan. */
  installments: number;
  /** Preview only: how many fall on or after the start. */
  remainingInstallments?: number | null;
  remainingAmount?: number | null;
  /** Preview only: installments (and their total) dated before today that an earlier "Charge fees from" would create. */
  backdatedInstallments?: number | null;
  backdatedAmount?: number | null;
}

/** What is chosen for one fee head while admitting a student; anything left out falls back to None / the structure's default. */
export interface FeeItemSelection {
  feeTypeId: number;
  installmentPlan?: string | null;
  isApplicable?: boolean | null;
}

/** One fee head a student of the class + year would get (admission form). */
export interface FeePreviewLine {
  feeTypeId: number;
  feeTypeName: string;
  feeTypeCode: string;
  /** True: the admission form offers an include / exclude checkbox. False: always applied. */
  isOptional: boolean;
  /** Per period of the structure's own frequency. */
  amount: number;
  structureFrequencyId: number;
  structureFrequencyName: string;
  periodsPerYear: number;
  dueDay?: number | null;
  effectiveFrom: string;
  allowedPlans: InstallmentPlanOption[];
}

export interface FeePreview {
  academicYearId: number;
  classId: number;
  /** False is a normal state: no fee structure for the class + year (lines is then empty). */
  hasFeeStructure: boolean;
  feeStructureId?: number | null;
  structureName?: string | null;
  /** The date the preview was worked out from. */
  chargeFrom: string;
  /** The charge-from date is before today: needs a confirmation and a reason. */
  isBackdated: boolean;
  backdatingAllowed: boolean;
  backdatingMessage?: string | null;
  lines: FeePreviewLine[];
}

export interface StudentFeeItem {
  studentFeeItemId: number;
  feeTypeId: number;
  feeTypeName: string;
  feeTypeCode: string;
  /** The billing cycle: always the fee line's frequency (amounts are per cycle). */
  frequencyId: number;
  frequencyName: string;
  structureFrequencyName: string;
  /** NONE | QUARTERLY | MONTHLY: how each cycle is split into installments. */
  installmentPlan: string;
  installmentPlanName: string;
  allowedPlans: InstallmentPlanOption[];
  effectiveFrom?: string | null;
  dueDay?: number | null;
  /** Optional fees can be switched on/off per student; non-optional ones always apply. */
  isOptional: boolean;
  baseAmount: number;
  concessionAmount: number;
  netAmount: number;
  /** Net for the whole academic year. */
  annualNetAmount: number;
  isApplicable: boolean;
  dueCount: number;
  nextDueDate?: string | null;
  totalDue: number;
  totalPaid: number;
}

export interface StudentFeeItems {
  studentId: string;
  academicYearId?: number | null;
  academicYearName?: string | null;
  items: StudentFeeItem[];
  /** Year total of the applicable items. */
  annualNetTotal: number;
}

export type FeeDueStatus = 'Paid' | 'Partially Paid' | 'Pending' | 'Overdue';

export interface StudentFeeDue {
  feeDueId: number;
  studentFeeItemId: number;
  /** Optional: an older server does not send it (then a concession for one fee type cannot be matched in the form; the server still checks). */
  feeTypeId?: number;
  feeTypeName: string;
  frequencyName: string;
  installmentPlan?: string;
  /** AUTO or BACKDATED. */
  source?: string;
  dueDate: string;
  amountDue: number;
  amountPaid: number;
  outstanding: number;
  /** Computed by the server on every read (never stored). */
  status: FeeDueStatus;
}

export interface StudentFeeDues {
  studentId: string;
  academicYearId?: number | null;
  academicYearName?: string | null;
  totalDue: number;
  totalPaid: number;
  outstanding: number;
  overdueAmount: number;
  overdueCount: number;
  dues: StudentFeeDue[];
}

export interface UpdateFeeItemRequest {
  installmentPlan?: string;
  isApplicable?: boolean;
}

const inr = (n: number) => '₹' + n.toLocaleString('en-IN', { maximumFractionDigits: 2 });

/** "month" / "quarter" for the installments of a plan. */
function planUnit(planCode: string, count: number): string {
  const unit = planCode === 'QUARTERLY' ? 'quarter' : 'month';
  return count === 1 ? unit : `${unit}s`;
}

/**
 * The note shown under an installment plan that starts part-way through a billing cycle: the FULL cycle amount is spread over the months (or quarters) of that cycle that are
 * left, so the year total never changes. Null when the plan starts at a cycle boundary (or is None).
 */
export function midCycleNote(planCode: string, installmentsPerCycle: number, remainingInstallments: number | null | undefined, cycleAmount: number): string | null {
  if (planCode === 'NONE' || installmentsPerCycle <= 1 || remainingInstallments == null) return null;
  const left = remainingInstallments % installmentsPerCycle;
  if (left === 0) return null;
  const unit = planUnit(planCode, left);
  return `The plan starts mid-cycle, so the full cycle amount is spread over the remaining ${unit} of that cycle: ${inr(cycleAmount)} over ${left} ${unit} = ${inr(Math.floor((cycleAmount / left) * 100) / 100)} each.`;
}

/** The general explanation (with an example) for the edit dialog, where the remaining installments are not known in advance. */
export function midCycleExample(planCode: string, installmentsPerCycle: number, cycleAmount: number): string | null {
  if (planCode === 'NONE' || installmentsPerCycle < 3) return null;
  const left = installmentsPerCycle - 2;
  const unit = planUnit(planCode, left);
  return `If a plan starts mid-cycle, the full cycle amount is spread over the remaining ${planUnit(planCode, 2)} of that cycle (for example ${inr(cycleAmount)} over ${left} ${unit} = ${inr(Math.floor((cycleAmount / left) * 100) / 100)} each).`;
}

/** Pill colour per due status, shared by every place that shows one. */
export function dueStatusTone(status: unknown): string {
  switch (status) {
    case 'Paid':
      return 'green';
    case 'Overdue':
      return 'red';
    case 'Pending':
      return 'yellow';
    case 'Partially Paid':
      return 'blue';
    default:
      return 'gray';
  }
}

export interface FeeItemRow extends Record<string, unknown> {
  studentFeeItemId: number;
  feeTypeName: string;
  frequencyName: string;
  planName: string;
  applicable: string;
  netAmount: string;
  annualNet: string;
  dues: string;
  paid: string;
  outstanding: string;
  nextDue: string;
}

export interface FeeDueRow extends Record<string, unknown> {
  feeDueId: number;
  dueDate: string;
  feeTypeName: string;
  amountDue: string;
  amountPaid: string;
  status: string;
}
