// Mirrors FamilyDtos.cs (api/Family). The parent screens read NOTHING else: every field here is one the Family endpoints return.

export type ChipState = 'PASSED_OUT' | 'LEFT' | 'TRANSFERRED_OUT' | 'PENDING_PROMOTION';

export interface FamilyChildCard {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  statusName: string;
  profilePhotoUrl?: string | null;
  academicYearName?: string | null;
  className?: string | null;
  sectionName?: string | null;
  rollNumber?: string | null;
  chipState?: ChipState | null;
  readOnly: boolean;
  /** Present / Absent, or null when today is not marked (or the child is not current). */
  attendanceToday?: string | null;
  feeDueNow: number;
  /** Unpaid from years before the one shown for this child (derived). Absent on an older server. */
  feeEarlierYears?: number;
}

export interface FamilyFeeChild { studentId: string; fullName: string; dueNow: number; earlierYears?: number }
export interface FamilyFeeTile { allPaid: boolean; totalDueNow: number; totalEarlierYears?: number; children: FamilyFeeChild[] }
export interface FamilyChildren { children: FamilyChildCard[]; feeTile: FamilyFeeTile }

export interface FamilyChildProfile {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  statusName: string;
  profilePhotoUrl?: string | null;
  academicYearName?: string | null;
  className?: string | null;
  sectionName?: string | null;
  rollNumber?: string | null;
  genderName?: string | null;
  /** ISO date-time */
  dateOfBirth: string;
  bloodGroupName?: string | null;
  address?: string | null;
}

export interface FamilyExam { termName: string; examName: string; examDate?: string | null }
export interface FamilyExamSchedule { schemeName: string; academicYearName: string; exams: FamilyExam[] }

export interface FamilyNotice { noticeId: number; title: string; body: string; publishedAt: string; isRead: boolean; postedBy?: string | null }

export interface FamilyEvent {
  eventId: number;
  title: string;
  startDate: string;
  endDate: string;
  isAllDay: boolean;
  startTime?: string | null;
  endTime?: string | null;
  categoryName: string;
  colorHex: string;
  description?: string | null;
}

/** StudentOwnAttendanceDto: Present / Absent only. */
export interface FamilyAttendance {
  from: string;
  to: string;
  summary: { markedDays: number; byStatus: Record<string, number>; presentPercentage?: number | null };
  days: { date: string; statusCode: string; statusName: string }[];
}

export interface FamilyHomework {
  homeworkId: number;
  subjectName: string;
  teacherName: string;
  title: string;
  description?: string | null;
  assignedDate: string;
  dueDate: string;
}

/** "Chips" in the order they read best. */
export function chipLabel(c: ChipState): string {
  switch (c) {
    case 'PASSED_OUT': return 'Passed out';
    case 'LEFT': return 'Left the school';
    case 'TRANSFERRED_OUT': return 'Transferred out';
    default: return 'Waiting to be promoted';
  }
}

/** The banner on a read-only child's dashboard. */
export function readOnlyBanner(c: ChipState | null | undefined): string | null {
  if (!c) return null;
  return c === 'PENDING_PROMOTION'
    ? 'This child has not been moved to the new class yet. You are seeing last year\'s record; it will update once the school promotes them.'
    : `${chipLabel(c)}: this is a read-only record of their time at the school. Nothing here can be changed.`;
}

export function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('');
}
