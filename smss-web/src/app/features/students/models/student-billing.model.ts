// Mirrors StudentFeeItemsDto / StudentFeeDuesDto / FeePreviewDto and the admission-time FeeItemSelection.

export interface FeeFrequencyOption {
  frequencyId: number;
  frequencyName: string;
  frequencyCode: string;
  /** Installments in a full academic year at this frequency. */
  installments: number;
  /** Preview only: how many of them fall on or after the student's start month (a mid-year admission pays fewer). */
  remainingInstallments?: number | null;
}

/** What is chosen for one fee head while admitting a student; anything left out falls back to the fee structure's default. */
export interface FeeItemSelection {
  feeTypeId: number;
  frequencyId?: number | null;
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
  allowedFrequencies: FeeFrequencyOption[];
}

export interface FeePreview {
  academicYearId: number;
  classId: number;
  /** False is a normal state: no fee structure for the class + year (lines is then empty). */
  hasFeeStructure: boolean;
  feeStructureId?: number | null;
  structureName?: string | null;
  lines: FeePreviewLine[];
}

export interface StudentFeeItem {
  studentFeeItemId: number;
  feeTypeId: number;
  feeTypeName: string;
  feeTypeCode: string;
  /** The frequency THIS student pays it at. */
  frequencyId: number;
  frequencyName: string;
  /** The frequency the fee structure charges it at (the amounts are per period of this). */
  structureFrequencyName: string;
  allowedFrequencies: FeeFrequencyOption[];
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
  feeTypeName: string;
  frequencyName: string;
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
  frequencyId?: number;
  isApplicable?: boolean;
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
