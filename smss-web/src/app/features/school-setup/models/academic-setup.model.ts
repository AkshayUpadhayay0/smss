// Mirrors YearOfferingDto / SaveYearOfferingRequest (S2): which classes and sections a school offers in one academic year.

export interface YearOffering {
  academicYearId: number;
  yearName: string;
  isCurrent: boolean;
  isClosed: boolean;
  offeredClasses: number;
  offeredSections: number;
  classes: OfferingClass[];
}

export interface OfferingClass {
  classId: number;
  className: string;
  sequenceOrder: number;
  /** The class master is Active. An inactive class cannot be newly offered. */
  isActive: boolean;
  offered: boolean;
  enrolments: number;
  hasFeeStructure: boolean;
  sections: OfferingSection[];
}

export interface OfferingSection {
  sectionId: number;
  sectionName: string;
  isActive: boolean;
  offered: boolean;
  masterMaxStrength?: number | null;
  /** This year's own capacity; null = the section master's. */
  maxStrength?: number | null;
  effectiveMaxStrength?: number | null;
  enrolments: number;
  hasClassTeacher: boolean;
  timetableEntries: number;
}

export interface SaveYearOfferingRequest {
  classes: { classId: number; sections: { sectionId: number; maxStrength: number | null }[] }[];
}

/** What the screen edits: the same shape as the server's, with the capacity as text so a half-typed number is never lost. */
export interface ClassDraft {
  classId: number;
  className: string;
  isActive: boolean;
  offered: boolean;
  /** Why this class can't be switched off ("12 students are enrolled"), or null. */
  lockedReason: string | null;
  sections: SectionDraft[];
}

export interface SectionDraft {
  sectionId: number;
  sectionName: string;
  isActive: boolean;
  offered: boolean;
  capacity: string;
  masterCapacity: number | null;
  lockedReason: string | null;
}

// ---------------- S3: subjects of one class in one year (mirrors YearClassSubjectsDto / SaveYearClassSubjectsRequest)

export interface YearClassSubjects {
  academicYearId: number;
  yearName: string;
  isCurrent: boolean;
  isClosed: boolean;
  classId: number;
  className: string;
  /** False when the class is not offered in the year: nothing can be mapped until it is. */
  classOffered: boolean;
  subjects: SetupSubject[];
  sections: SetupSection[];
}

export interface SetupSubject {
  subjectId: number;
  subjectName: string;
  subjectCode?: string | null;
  isActive: boolean;
  mapped: boolean;
  /** Timetable slots, homework, tests, marks and exam setups in this year that use the subject for this class. */
  inUse: number;
  inUseText?: string | null;
}

export interface SetupSection {
  sectionId: number;
  sectionName: string;
  extraSubjectIds: number[];
  notTaughtSubjectIds: number[];
  inUseSubjectIds: number[];
  effectiveCount: number;
}

export interface SaveYearClassSubjectsRequest {
  subjectIds: number[];
  sections: { sectionId: number; extraSubjectIds: number[]; notTaughtSubjectIds: number[] }[];
}

/** What the subjects screen edits. */
export interface SubjectDraft {
  mapped: Set<number>;
  sections: { sectionId: number; sectionName: string; extra: Set<number>; notTaught: Set<number> }[];
}

// ---------------- S4: bell schedules and which schedule a class follows on which day (mirrors ScheduleDto / YearSchedulesDto)

export interface Schedule {
  scheduleId: number;
  scheduleName: string;
  isDefault: boolean;
  slots: ScheduleSlot[];
  assignments: number;
  timetableEntries: number;
  /** Null = it can be deleted; otherwise why not. */
  deleteBlockedReason?: string | null;
}

export interface ScheduleSlot {
  periodId: number;
  slotType: 'PERIOD' | 'BREAK';
  periodNumber?: number | null;
  label?: string | null;
  /** "HH:mm:ss" */
  startTime: string;
  endTime: string;
  slotOrder: number;
  isActive: boolean;
  /** Active timetable entries on this slot (a slot in use can't be deactivated, deleted or turned into a break). */
  activeEntryCount: number;
}

export interface ScheduleSlotRequest {
  slotType: 'PERIOD' | 'BREAK';
  periodNumber: number | null;
  label: string | null;
  startTime: string;
  endTime: string;
}

export interface YearSchedules {
  academicYearId: number;
  yearName: string;
  isClosed: boolean;
  defaultScheduleId?: number | null;
  schedules: { scheduleId: number; scheduleName: string; isDefault: boolean }[];
  classes: { classId: number; className: string }[];
  rows: ScheduleAssignmentRow[];
}

export interface ScheduleAssignmentRow {
  /** Null = every class the year offers. */
  classId: number | null;
  className?: string | null;
  /** Null = every day; 1 = Monday .. 6 = Saturday. */
  dayOfWeek: number | null;
  scheduleId: number;
  scheduleName?: string;
}

export interface SaveYearSchedulesRequest {
  rows: { classId: number | null; dayOfWeek: number | null; scheduleId: number }[];
}

// ---------------- S5: class teachers with history (mirrors ClassTeacherListDto / ClassTeacherAssignmentDto)

export interface ClassTeacherList {
  academicYearId: number;
  academicYearName: string;
  sections: ClassTeacherSection[];
  unassignedCount: number;
}

export interface ClassTeacherSection {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  assignmentId?: number | null;
  employeeId?: string | null;
  employeeName?: string | null;
  hasClassTeacher: boolean;
  /** "yyyy-MM-dd": the day the current teacher started. */
  effectiveFrom?: string | null;
}

export interface EligibleTeacher {
  employeeId: string;
  fullName: string;
  designationName: string;
}

export interface ClassTeacherAssignment {
  assignmentId: number;
  employeeId: string;
  employeeName: string;
  statusName: string;
  effectiveFrom: string;
  /** Null = still current. */
  effectiveTo?: string | null;
  assignReason?: string | null;
  assignedByName?: string | null;
  endedByName?: string | null;
  endReason?: string | null;
}

export interface AssignClassTeacherRequest {
  academicYearId: number;
  sectionId: number;
  employeeId: string;
  reason: string | null;
  confirmMultipleSections: boolean;
}
