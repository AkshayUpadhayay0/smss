// Mirrors FeeDashboardDto / ClassFeeReportDto (read-only fee reports).

export interface FeeCollectionMonth {
  /** First day of the month, e.g. 2026-10-01. */
  month: string;
  label: string;
  /** Fee collected (fee only: never includes a late fee or tax). */
  amount: number;
  /** Late fee collected in the month on installments of the year, without its tax. Null where it could not be read. */
  lateFee?: number | null;
  /** Total received in the month = fee + late fee + tax (voided payments excluded). */
  totalReceived?: number | null;
}

export interface FeeTaxBreakdown {
  taxTypeId: number;
  taxName: string;
  taxCode: string;
  taxAmount: number;
  paymentCount: number;
}

export interface FeeConcessionBreakdown {
  concessionId: number;
  concessionName: string;
  concessionCode: string;
  concessionAmount: number;
  paymentCount: number;
}

export interface FeeDashboard {
  academicYearId: number;
  academicYearName: string;
  classId?: number | null;
  sectionId?: number | null;
  upcomingDays: number;
  asOf: string;
  totalDue: number;
  /** CASH collected: neither tax nor the concession is in it. */
  totalCollected: number;
  /** Discount given at payment time on Active payments in the same scope. due = collected + concessionGiven + outstanding. Optional so an older response still reads. */
  concessionGiven?: number;
  concessionByType?: FeeConcessionBreakdown[];
  /** Tax collected on Active payments in the same scope (reported on its own). Optional so an older response still reads. */
  taxCollected?: number;
  taxByType?: FeeTaxBreakdown[];
  totalOutstanding: number;
  overdueAmount: number;
  overdueCount: number;
  upcomingAmount: number;
  upcomingCount: number;
  collectionByMonth: FeeCollectionMonth[];
  /** False for the Principal: same numbers, no drill-down / export. */
  canManage: boolean;
}

export type OverallFeeStatus = 'Overdue' | 'Partially Paid' | 'Pending' | 'Paid' | 'Not Set';

export interface ClassFeeRow {
  studentId: string;
  fullName: string;
  rollNumber?: string | null;
  sectionName: string;
  totalDue: number;
  /** Cash collected: paid against the installments minus the concession given. */
  totalCollected: number;
  /** Discount given at payment time (Active payments, this year). Optional so an older response still reads. */
  concession?: number;
  outstanding: number;
  status: OverallFeeStatus;
}

export interface ClassFeeReport {
  /** yyyy-MM when the report was asked for one month of dues; null/absent for the whole year. */
  month?: string | null;
  academicYearId: number;
  academicYearName: string;
  classId: number;
  className: string;
  sectionId?: number | null;
  students: ClassFeeRow[];
  totalDue: number;
  totalCollected: number;
  totalConcession?: number;
  totalOutstanding: number;
  statusCounts: Record<string, number>;
  canManage: boolean;
}

/** What a class report table row holds: display strings plus the raw numbers the sorting uses. */
export interface ClassFeeTableRow extends Record<string, unknown> {
  studentId: string;
  fullName: string;
  sectionName: string;
  rollNumber: string;
  totalDue: string;
  totalCollected: string;
  concession: string;
  outstanding: string;
  status: string;
  totalDueNum: number;
  totalCollectedNum: number;
  concessionNum: number;
  outstandingNum: number;
}

/** Worst first, so sorting by status puts the students who need a call at the top. */
export const STATUS_RANK: Record<string, number> = { Overdue: 0, 'Partially Paid': 1, Pending: 2, Paid: 3, 'Not Set': 4 };

/** One CSV cell: quoted, and a leading = + - @ is defused so a spreadsheet never runs a typed name as a formula. */
export function csvCell(value: unknown): string {
  let s = String(value ?? '');
  if (/^[=+\-@\t\r]/.test(s)) s = "'" + s;
  return '"' + s.replace(/"/g, '""') + '"';
}

/** One payment that carried a concession (GET /api/FeeReports/concessions-given). */
export interface ConcessionGivenRow {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  studentId: string;
  studentName: string;
  concessionName: string;
  concessionCode: string;
  concessionType: 'PERCENTAGE' | 'FIXED';
  concessionValue: number;
  concessionAmount: number;
  reason?: string | null;
  appliedByName?: string | null;
  /** A voided payment stays in the list (it is the audit trail) but is not in the total. */
  status: 'Active' | 'Voided';
}

export interface ConcessionsGiven {
  items: ConcessionGivenRow[];
  page: number;
  pageSize: number;
  totalItems: number;
  /** Sum of the Active payments' concession over the whole date range (not only this page). */
  totalAmount: number;
}

/** One late fee charged by one payment on one due (GET /api/LateFee/report, view = charged). */
export interface LateFeeChargedItem {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  studentId: string;
  studentName: string;
  className: string | null;
  feeType: string;
  dueDate: string;
  daysLate: number;
  /** The days beyond the grace days: the ones that were charged. */
  daysCharged: number;
  baseAmount: number;
  amount: number;
}

/** A late fee a payment would have charged and the cashier waived (view = waived). */
export interface LateFeeWaivedItem {
  paymentId: number;
  receiptNumber: string;
  paymentDate: string;
  studentId: string;
  studentName: string;
  className: string | null;
  wouldHaveBeen: number;
  waivedBy: string | null;
  note: string | null;
}

export interface LateFeeReport {
  from: string;
  to: string;
  classId: number | null;
  view: 'charged' | 'waived';
  page: number;
  pageSize: number;
  /** Rows of the viewed list over the whole range. */
  totalItems: number;
  /** Always both, whichever list is viewed. Voided payments are in neither; the charged amount has no tax. */
  chargedCount: number;
  chargedAmount: number;
  waivedCount: number;
  waivedAmount: number;
  charged: LateFeeChargedItem[];
  waived: LateFeeWaivedItem[];
}

