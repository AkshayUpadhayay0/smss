import { FeeCollectionMonth } from './dashboard-shared.model';

/** GET /api/AccountantDashboard/summary (Accountant only). Mirrors AccountantDashboardDtos.cs. */

export type AccountantDashboardPart = 'fees' | 'collectedToday' | 'payments' | 'lateFee';

/** Every number is null when its part is in partsUnavailable. */
export interface AccountantDashboardStats {
  /** CASH received today (India date), whatever year it paid for. */
  collectedToday: number | null;
  /** CASH received this month, summed over every year's installments (the last bar of the chart). */
  collectedThisMonth: number | null;
  /** LATE FEE collected today / this month (all years, not voided), without its tax. Separate from the cash figures above, which are fee only. */
  lateFeeToday: number | null;
  lateFeeThisMonth: number | null;
  /** TOTAL RECEIVED today / this month = what the non-voided receipts add up to (fee + late fee + tax): the figure the cash drawer reconciles against. */
  totalReceivedToday: number | null;
  totalReceivedThisMonth: number | null;
  outstanding: number | null;
  overdueCount: number | null;
  overdueAmount: number | null;
  upcomingCount: number | null;
  upcomingAmount: number | null;
  /** The upcoming window, in days. */
  upcomingDays: number;
}

export interface DashboardPayment {
  paymentId: number;
  receiptNumber: string;
  studentId: string;
  studentName: string;
  amount: number;
  paymentMode: string;
  paymentDate: string;
}

export interface AccountantDashboard {
  today: string;
  /** The academic year being VIEWED: it decides the DUES cards. The cash cards are always all-years cash. */
  academicYearId: number | null;
  academicYearName: string | null;
  academicYearIsCurrent: boolean;
  academicYearIsClosed: boolean;
  currentYearName: string | null;
  /** A sentence about the viewed year when it is not the current one; null otherwise. */
  viewingNote: string | null;
  /** false = the viewed year has no fee items: the dues cards say "No fee structure yet". */
  hasFeeSetup: boolean;
  stats: AccountantDashboardStats;
  /** The last 6 months, oldest first, this month last. Empty when fees are unavailable. */
  collectionTrend: FeeCollectionMonth[];
  recentPayments: DashboardPayment[];
  partsUnavailable: AccountantDashboardPart[];
}
