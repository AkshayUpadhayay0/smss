// Mirrors StudentDashboardDto (GET /api/StudentDashboard): everything the Student dashboard shows, in one response.
import { SchoolEvent } from '../../calendar/models/calendar.model';
import { StudentLeave } from '../../leave/models/leave.model';
import { Notice } from '../../notices/models/notice.model';
import { StudentResult } from '../../assessments/models/assessment.model';

export interface DashboardProfile {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  /** Class / section / roll number of the current academic year; null when not enrolled in it. */
  className?: string | null;
  sectionName?: string | null;
  rollNumber?: string | null;
  academicYearName?: string | null;
  /** Relative upload path; run it through toUploadUrl(). */
  profilePhotoUrl?: string | null;
}

/** Present / Absent are the only attendance statuses that exist. */
export interface DashboardAttendance {
  presentCount: number;
  absentCount: number;
  totalMarkedDays: number;
  /** One decimal; null when nothing is marked yet. */
  presentPercentage?: number | null;
  from: string;
  to: string;
}

export type ClassStatus = 'Completed' | 'InProgress' | 'Upcoming';

export interface TodayClass {
  periodNumber: number;
  subjectName: string;
  teacherName: string;
  roomNo?: string | null;
  /** HH:mm:ss */
  startTime: string;
  endTime: string;
  status: ClassStatus;
}

export interface StudentDashboard {
  profile: DashboardProfile;
  /** Null when the attendance part could not be loaded. */
  attendance?: DashboardAttendance | null;
  eventsCount: number;
  unreadNoticesCount: number;
  /** yyyy-MM-dd, the school's (India) today. */
  today: string;
  todayClasses: TodayClass[];
  recentNotices: Notice[];
  upcomingEvents: SchoolEvent[];
  recentResults: StudentResult[];
  recentLeave: StudentLeave[];
}
