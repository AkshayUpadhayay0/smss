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
}

export interface TimetableCell {
  dayOfWeek: number;
  entry: TimetableEntry | null;
}

export interface TimetableRow {
  period: { periodId: number; periodNumber: number; startTime: string; endTime: string };
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
