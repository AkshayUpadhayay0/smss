// Mirrors HomeworkDto / CreateHomeworkRequest / UpdateHomeworkRequest (homework v1: publish + view only; no submissions, no marks).

export interface Homework {
  homeworkId: number;
  academicYearId: number;
  academicYearName: string;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  subjectId: number;
  subjectName: string;
  employeeId: string;
  teacherName: string;
  title: string;
  description?: string | null;
  /** yyyy-MM-dd */
  assignedDate: string;
  dueDate: string;
  hasAttachment: boolean;
  /** The name the file downloads as; the storage path is never exposed. */
  attachmentFileName?: string | null;
  statusName: string;
}

export interface CreateHomeworkRequest {
  academicYearId?: number;
  sectionId: number;
  subjectId: number;
  /** Only a School Admin names the owning teacher; a teacher always owns their own. */
  employeeId?: string;
  title: string;
  description: string | null;
  assignedDate?: string;
  dueDate: string;
}

export interface UpdateHomeworkRequest {
  title: string;
  description: string | null;
  assignedDate: string;
  dueDate: string;
}

export interface HomeworkFilters {
  academicYearId?: number;
  sectionId?: number;
  subjectId?: number;
  /** Dates filter on the due date, inclusive. */
  from?: string;
  to?: string;
}

/** A section + subject a teacher can set homework for, taken from their own timetable. */
export interface TeachingCombo {
  sectionId: number;
  sectionLabel: string;
  subjectId: number;
  subjectName: string;
}

export const MAX_ATTACHMENT_BYTES = 5 * 1024 * 1024;
export const ATTACHMENT_ACCEPT = '.pdf,.png,.jpg,.jpeg,.webp';

/** A row of the teacher / admin table: display-ready values. */
export interface HomeworkRow extends Record<string, unknown> {
  homeworkId: number;
  title: string;
  group: string;
  subjectName: string;
  teacherName: string;
  assigned: string;
  due: string;
  hasAttachment: boolean;
}
