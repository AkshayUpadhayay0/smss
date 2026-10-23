// Mirrors SectionAttendanceDto / MySectionsDto / SubmitAttendanceResultDto (daily attendance).

export interface AttendanceStatus {
  attendanceStatusId: number;
  statusCode: string;
  statusName: string;
}

export interface MarkableSection {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  rosterCount: number;
  markedToday: boolean;
}

export interface MySections {
  today: string;
  academicYearId: number;
  academicYearName: string;
  sections: MarkableSection[];
}

export interface SectionAttendanceStudent {
  studentId: string;
  fullName: string;
  rollNumber?: string | null;
  /** null = not marked yet. */
  attendanceStatusId?: number | null;
  statusCode?: string | null;
  statusName?: string | null;
  markedByName?: string | null;
  markedAt?: string | null;
  isCorrected: boolean;
  correctionReason?: string | null;
  onRoster: boolean;
}

export interface SectionAttendance {
  sectionId: number;
  classId: number;
  className: string;
  sectionName: string;
  academicYearId: number;
  academicYearName: string;
  date: string;
  isToday: boolean;
  rosterCount: number;
  markedCount: number;
  isMarked: boolean;
  canSubmit: boolean;
  statuses: AttendanceStatus[];
  students: SectionAttendanceStudent[];
}

export interface SubmitAttendanceRequest {
  sectionId: number;
  date: string;
  records: { studentId: string; attendanceStatusId: number }[];
}

export interface SubmitAttendanceResult {
  sectionId: number;
  academicYearId: number;
  date: string;
  total: number;
  present: number;
  absent: number;
  created: number;
  updated: number;
}

export const PRESENT = 'PRESENT';
export const ABSENT = 'ABSENT';
