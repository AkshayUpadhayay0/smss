import { SchoolEvent } from '../../calendar/models/calendar.model';

/** GET /api/SchoolDashboard/summary (School Admin only). Mirrors SchoolDashboardDtos.cs. */

/** Parts the server could not load; the screen shows "Unavailable" for them instead of a wrong number. */
export type SchoolDashboardPart =
  | 'students'
  | 'employees'
  | 'attendance'
  | 'fees'
  | 'admissions'
  | 'employeeLeave'
  | 'studentLeave'
  | 'classTeachers'
  | 'events'
  | 'notices';

/** Every number is null when its part is in partsUnavailable. */
export interface SchoolDashboardStats {
  totalStudents: number | null;
  totalEmployees: number | null;
  attendanceMarkedToday: number | null;
  attendancePresentToday: number | null;
  /** Present / marked x 100 (one decimal); null when nothing is marked today. */
  attendancePercentToday: number | null;
  sectionsMarkedToday: number | null;
  sectionsTotal: number | null;
  feeCollectedThisMonth: number | null;
  feeOutstanding: number | null;
  pendingAdmissions: number | null;
}

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

export interface SchoolDashboardAlerts {
  pendingEmployeeLeaves: number | null;
  pendingStudentLeaves: number | null;
  sectionsWithoutClassTeacher: number | null;
  feeOverdueCount: number | null;
  feeOverdueAmount: number | null;
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

export interface SchoolDashboard {
  today: string;
  academicYearId: number | null;
  academicYearName: string | null;
  stats: SchoolDashboardStats;
  /** Always 14 days, oldest first, today last. Empty when the attendance part is unavailable. */
  attendanceTrend: AttendanceTrendPoint[];
  /** Always 6 months, oldest first, this month last. Empty when the fees part is unavailable. */
  feeCollectionTrend: FeeCollectionMonth[];
  alerts: SchoolDashboardAlerts;
  upcomingEvents: SchoolEvent[];
  recentNotices: DashboardNotice[];
  recentAdmissions: DashboardAdmission[];
  partsUnavailable: SchoolDashboardPart[];
}
