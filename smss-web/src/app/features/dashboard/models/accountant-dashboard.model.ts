import { FeeCollectionMonth } from './school-dashboard.model';

/** GET /api/AccountantDashboard/summary (Accountant only). Mirrors AccountantDashboardDtos.cs. */

export type AccountantDashboardPart = 'fees' | 'collectedToday' | 'payments';

/** Every number is null when its part is in partsUnavailable. */
export interface AccountantDashboardStats {
  /** Active payments received today (India date). */
  collectedToday: number | null;
  /** The Fee Dashboard's figure for the current month (the last bar of the chart). */
  collectedThisMonth: number | null;
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
  academicYearId: number | null;
  academicYearName: string | null;
  stats: AccountantDashboardStats;
  /** The last 6 months, oldest first, this month last. Empty when fees are unavailable. */
  collectionTrend: FeeCollectionMonth[];
  recentPayments: DashboardPayment[];
  partsUnavailable: AccountantDashboardPart[];
}
