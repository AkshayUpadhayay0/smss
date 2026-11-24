/** Small types more than one role's dashboard shares (the Principal, Accountant and Admission dashboards). Mirrors DashboardSharedDtos.cs and FeeDashboardDtos.cs. */

export interface AttendanceTrendPoint {
  /** yyyy-MM-dd */
  date: string;
  marked: number;
  present: number;
  /** null for a day nothing was marked (a Sunday, a holiday, or today before marking). */
  presentPercent: number | null;
}

export interface FeeCollectionMonth {
  /** First day of the month, yyyy-MM-dd. */
  month: string;
  /** "Oct 2026" */
  label: string;
  amount: number;
}

export interface DashboardNotice {
  noticeId: number;
  title: string;
  audience: string;
  createdByName: string | null;
  publishedAt: string;
}

export interface DashboardAdmission {
  admissionId: number;
  applicationNumber: string;
  applicantFullName: string;
  appliedClassName: string | null;
  /** APPLICATION_SUBMITTED / UNDER_REVIEW / APPROVED / REJECTED / ADMITTED / CANCELLED */
  statusName: string;
  applicationDate: string;
}
