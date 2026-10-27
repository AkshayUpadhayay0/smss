/** GET /api/TeacherDashboard (Teacher role only). Mirrors TeacherDashboardDtos.cs. */

export interface TeacherProfile {
  employeeId: string;
  fullName: string;
  designationName: string;
  departmentName: string;
  /** Relative /uploads/... path; render with toUploadUrl(). */
  photoUrl: string | null;
}

export interface TeacherSection {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
}

/** Parts the server could not load; the screen shows "Unavailable" for them instead of a wrong number. */
export type TeacherDashboardPart = 'classTeacherOf' | 'subjectsTaught' | 'totalStudents' | 'feePending' | 'attendance';

export interface TeacherDashboard {
  today: string;
  profile: TeacherProfile;
  /** Empty = not a class teacher. */
  classTeacherOf: TeacherSection[];
  subjectsTaught: string[];
  /** The four numbers below are null when classTeacherOf is empty ("not applicable"). */
  totalStudents: number | null;
  /** A head count of students with an unpaid installment due this month. No amounts, no names. */
  feePending: number | null;
  presentToday: number | null;
  absentToday: number | null;
  /** Class-teacher sections exist and none has been marked today (presentToday / absentToday are then null). */
  notMarkedYet: boolean;
  /** With several sections: how many are still unmarked today. */
  unmarkedSections: number;
  partsUnavailable: TeacherDashboardPart[];
}
