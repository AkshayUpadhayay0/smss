// Mirrors FeeDashboardDto / ClassFeeReportDto (read-only fee reports).

export interface FeeCollectionMonth {
  /** First day of the month, e.g. 2026-10-01. */
  month: string;
  label: string;
  amount: number;
}

export interface FeeDashboard {
  academicYearId: number;
  academicYearName: string;
  classId?: number | null;
  sectionId?: number | null;
  upcomingDays: number;
  asOf: string;
  totalDue: number;
  totalCollected: number;
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
  totalCollected: number;
  outstanding: number;
  status: OverallFeeStatus;
}

export interface ClassFeeReport {
  academicYearId: number;
  academicYearName: string;
  classId: number;
  className: string;
  sectionId?: number | null;
  students: ClassFeeRow[];
  totalDue: number;
  totalCollected: number;
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
  outstanding: string;
  status: string;
  totalDueNum: number;
  totalCollectedNum: number;
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
