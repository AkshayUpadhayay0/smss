// Mirrors PeriodDto / TimetableGridDto / TimetableEntryDto / TimetableLookupsDto (the weekly timetable). Times arrive as "HH:mm:ss".

export interface Period {
  periodId: number;
  periodNumber: number;
  startTime: string;
  endTime: string;
  statusName: string;
  /** Active timetable entries using this period (a period in use cannot be deactivated). */
  activeEntryCount: number;
}

export interface PeriodRequest {
  periodNumber: number;
  startTime: string;
  endTime: string;
}

export interface TimetableEntry {
  timetableEntryId: number;
  academicYearId: number;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  /** 1 = Monday .. 6 = Saturday */
  dayOfWeek: number;
  periodId: number;
  periodNumber: number;
  subjectId: number;
  subjectName: string;
  subjectCode?: string | null;
  employeeId: string;
  employeeName: string;
  roomNo?: string | null;
  /** The slot was copied without a teacher and still needs one. */
  needsTeacher?: boolean;
  timetableId?: number;
}

export interface TimetableCell {
  dayOfWeek: number;
  entry: TimetableEntry | null;
  /** False when the slot is not part of the schedule the class follows on this day (nothing can be assigned there). */
  applies?: boolean;
}

export interface TimetableRow {
  period: {
    periodId: number;
    /** 0 for a break. */
    periodNumber: number;
    startTime: string;
    endTime: string;
    /** PERIOD or BREAK (a break never holds a lesson). */
    slotType?: 'PERIOD' | 'BREAK';
    label?: string | null;
    scheduleId?: number;
    /** Shown only when the grid mixes several schedules. */
    scheduleName?: string | null;
    days?: number[];
  };
  cells: TimetableCell[];
}

export interface TimetableGrid {
  academicYearId: number;
  academicYearName: string;
  scope: 'section' | 'teacher';
  sectionId?: number | null;
  classId?: number | null;
  className?: string | null;
  sectionName?: string | null;
  employeeId?: string | null;
  employeeName?: string | null;
  /** Which timetable (version) this grid shows. */
  timetableId?: number | null;
  timetableName?: string | null;
  timetableState?: 'DRAFT' | 'ACTIVE' | 'ARCHIVED' | null;
  days: { dayOfWeek: number; name: string }[];
  rows: TimetableRow[];
  entryCount: number;
}

export interface TimetableLookups {
  subjects: { subjectId: number; subjectName: string; subjectCode?: string | null }[];
  teachers: { employeeId: string; fullName: string; designationName: string }[];
}

export interface AssignTimetableRequest {
  academicYearId: number;
  sectionId: number;
  dayOfWeek: number;
  periodId: number;
  subjectId: number;
  employeeId: string;
  roomNo: string | null;
}

export interface UnassignTimetableRequest {
  academicYearId: number;
  sectionId: number;
  dayOfWeek: number;
  periodId: number;
}

// ---------------- S6: timetable versions (mirrors TimetableSummaryDto and friends)

export interface TimetableSummary {
  timetableId: number;
  academicYearId: number;
  classId: number;
  sectionId: number;
  timetableName: string;
  scheduleId: number;
  scheduleName: string;
  state: 'DRAFT' | 'ACTIVE' | 'ARCHIVED';
  isActive: boolean;
  /** Was active at some time: archived, never deleted. */
  everActive: boolean;
  slotCount: number;
  needsTeacherCount: number;
  createdAt: string;
  updatedAt: string;
  activatedAt?: string | null;
  activatedByName?: string | null;
  createdByName?: string | null;
  canDelete: boolean;
}

export interface TimetableProblem {
  severity: 'ERROR' | 'WARNING';
  code: string;
  message: string;
  dayOfWeek?: number | null;
  periodId?: number | null;
}

export interface TimetableValidation {
  timetableId: number;
  timetableName: string;
  canActivate: boolean;
  slotCount: number;
  slotsWithoutTeacher: number;
  errors: TimetableProblem[];
  warnings: TimetableProblem[];
}

export interface TimetableActivation {
  timetable: TimetableSummary;
  previousTimetableName?: string | null;
  slotsWithoutTeacher: number;
}

export interface ApplySlotNote {
  dayOfWeek: number;
  periodNumber: number;
  subjectName: string;
  reason: string;
}

export interface ApplySectionResult {
  sectionId: number;
  sectionName: string;
  created: boolean;
  timetableId?: number | null;
  timetableName: string;
  skipReason?: string | null;
  slotsCopied: number;
  slotsNeedingTeacher: number;
  slotsSkipped: number;
  needsTeacher: ApplySlotNote[];
  skipped: ApplySlotNote[];
}

export interface ApplyResult {
  sourceTimetableId: number;
  preview: boolean;
  sections: ApplySectionResult[];
}

export interface AssignSlotRequest {
  dayOfWeek: number;
  periodId: number;
  subjectId: number;
  /** Optional here: a slot without a teacher is flagged "needs a teacher". */
  employeeId: string | null;
  roomNo: string | null;
}
