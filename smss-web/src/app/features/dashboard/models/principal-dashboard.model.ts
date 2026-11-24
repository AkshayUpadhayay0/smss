import { SchoolEvent } from '../../calendar/models/calendar.model';
import { AttendanceTrendPoint, DashboardNotice } from './school-dashboard.model';

/** GET /api/PrincipalDashboard/summary (Principal only). Mirrors PrincipalDashboardDtos.cs. */

export type PrincipalDashboardPart = 'students' | 'staff' | 'attendance' | 'fees' | 'employeeLeave' | 'admissions' | 'results' | 'events' | 'notices';

/** Every number is null when its part is in partsUnavailable. */
export interface PrincipalDashboardStats {
  totalStudents: number | null;
  totalStaff: number | null;
  attendanceMarkedToday: number | null;
  attendancePresentToday: number | null;
  /** Present / marked x 100 (one decimal); null when nothing is marked today. */
  attendancePercentToday: number | null;
  feeTotalDue: number | null;
  feeCollected: number | null;
  feeOutstanding: number | null;
}

export interface PrincipalDashboardAlerts {
  pendingEmployeeLeaves: number | null;
  /** Applications Under Review: the ones the Principal can approve or reject. */
  admissionsToDecide: number | null;
  /** Applications just submitted, not yet taken into review. */
  admissionsAwaitingReview: number | null;
}

export interface DashboardPublication {
  publicationId: number;
  className: string;
  sectionName: string;
  /** TERM or YEAR */
  scope: string;
  termName: string | null;
  version: number;
  status: string;
  publishedAt: string;
  studentCount: number;
}

export interface PrincipalDashboard {
  today: string;
  /** The academic year being VIEWED (the one asked for, else the school's current year). */
  academicYearId: number | null;
  academicYearName: string | null;
  academicYearIsCurrent: boolean;
  academicYearIsClosed: boolean;
  /** The school's current year name. */
  currentYearName: string | null;
  /** A sentence about the viewed year when it is not the current one; null otherwise. */
  viewingNote: string | null;
  /** false = the viewed year has no fee items: the fee figures are null and the page says "No fee structure yet". */
  hasFeeSetup: boolean;
  stats: PrincipalDashboardStats;
  /** The last 14 days, oldest first. Empty when attendance is unavailable. */
  attendanceTrend: AttendanceTrendPoint[];
  alerts: PrincipalDashboardAlerts;
  recentPublications: DashboardPublication[];
  upcomingEvents: SchoolEvent[];
  recentNotices: DashboardNotice[];
  partsUnavailable: PrincipalDashboardPart[];
}
