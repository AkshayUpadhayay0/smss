// Mirrors SectionFeeStatusDto / SectionFeeGridDto (GET /api/Fee/section-status and /section-status/months): one MONTH of a class's fees, read-only.
// "A month" = the instalments whose due date falls in it; "paid" = what the payment allocations have applied to them. No receipts, modes or concessions.

export type MonthStatus = 'Paid' | 'Partially paid' | 'Unpaid' | 'Overdue' | 'Not due yet' | 'No fee this month';

export interface SectionFeeMonth {
  /** yyyy-MM */
  month: string;
  label: string;
  isCurrent: boolean;
}

export interface SectionFeeSummary {
  students: number;
  paid: number;
  partial: number;
  unpaid: number;
  overdue: number;
  notDueYet: number;
  noFeeThisMonth: number;
  /** Students with no fee structure at all (a subset of noFeeThisMonth). */
  noFeeAssigned: number;
  dueTotal: number;
  collectedTotal: number;
}

export interface SectionFeeStudent {
  rollNumber?: string | null;
  fullName: string;
  monthStatus: MonthStatus;
  dueThisMonth: number;
  paidThisMonth: number;
  balanceThisMonth: number;
  /** Every unpaid instalment up to the end of the selected month: earlier months plus this one. */
  totalOutstandingTillDate: number;
}

export interface SectionFeeStatus {
  sectionId: number;
  className: string;
  sectionName: string;
  academicYearId: number;
  academicYearName: string;
  month: string;
  monthLabel: string;
  monthInAcademicYear: boolean;
  isCurrentMonth: boolean;
  months: SectionFeeMonth[];
  summary: SectionFeeSummary;
  students: SectionFeeStudent[];
}

/** P paid, Partial, U unpaid, O overdue, N not due yet, - no fee that month. */
export type GridCode = 'P' | 'Partial' | 'U' | 'O' | 'N' | '-';

export interface SectionFeeGridRow {
  rollNumber?: string | null;
  fullName: string;
  /** One code per month, in the order of `months`. */
  cells: GridCode[];
}

export interface SectionFeeGrid {
  sectionId: number;
  className: string;
  sectionName: string;
  academicYearId: number;
  academicYearName: string;
  months: SectionFeeMonth[];
  students: SectionFeeGridRow[];
}
