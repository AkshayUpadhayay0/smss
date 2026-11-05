// Mirrors AttendanceReportDtos.cs (GET /api/Attendance/report/monthly and /report/options).

export type ReportMark = 'Present' | 'Absent' | 'NotMarked' | 'NotEnrolled' | 'NonWorking';

export interface ReportDay {
  /** yyyy-MM-dd */
  date: string;
  /** 1 = Monday ... 7 = Sunday */
  dayOfWeek: number;
  isWorkingDay: boolean;
  /** "Sunday", "Holiday" or "Outside academic year"; null on a working day. */
  nonWorkingReason?: string | null;
  holidayName?: string | null;
  isFuture: boolean;
  markedCount: number;
}

export interface ReportStudent {
  studentId: string;
  fullName: string;
  rollNumber?: string | null;
  /** False for a student who has left but still has marks that month. */
  isActive: boolean;
  enrolledFrom?: string | null;
  /** One entry per day, same order as `days`. */
  marks: ReportMark[];
  presentDays: number;
  absentDays: number;
  notMarkedDays: number;
  /** One decimal; null when no working day is marked for the student. */
  percentage?: number | null;
  belowThreshold: boolean;
}

export interface ReportSummary {
  studentCount: number;
  averagePercentage?: number | null;
  studentsBelowThreshold: number;
  thresholdPercentage: number;
  workingDays: number;
  daysNotMarked: number;
  notMarkedDates: string[];
}

export interface MonthlyAttendanceReport {
  sectionId: number;
  classId: number;
  className: string;
  sectionName: string;
  academicYearId: number;
  academicYearName: string;
  year: number;
  month: number;
  monthName: string;
  today: string;
  days: ReportDay[];
  students: ReportStudent[];
  summary: ReportSummary;
}

export interface ReportYearOption {
  academicYearId: number;
  yearName: string;
  isCurrent: boolean;
  startDate: string;
  endDate: string;
}

export interface ReportSectionOption {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
}

export interface ReportOptions {
  academicYearId: number;
  years: ReportYearOption[];
  sections: ReportSectionOption[];
  /** True for the School Admin and Principal (every section); false for a class teacher (their own). */
  allSections: boolean;
}
