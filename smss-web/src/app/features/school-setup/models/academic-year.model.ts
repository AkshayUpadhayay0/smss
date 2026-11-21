// Mirrors AcademicYearResponse / Create+UpdateAcademicYearRequest. Dates are yyyy-MM-dd strings.

export interface AcademicYear {
  academicYearId: number;
  schoolId: string;
  yearName: string;
  startDate: string;
  endDate: string;
  isCurrent: boolean;
  statusId?: number | null;
  /** "Active" / "Inactive", resolved by the server so the client never deals in status ids. */
  statusName?: string | null;
  createdAt: string;
  updatedAt: string;
  /** Derived by the server: CLOSED | CURRENT | UPCOMING | PAST | IN_SESSION. */
  state: AcademicYearState;
  isClosed: boolean;
  closedAt?: string | null;
  closeNote?: string | null;
  /** What already depends on the year (counts), so the screen can explain why a change is refused. */
  usage: AcademicYearUsage;
}

export type AcademicYearState = 'CLOSED' | 'CURRENT' | 'UPCOMING' | 'PAST' | 'IN_SESSION';

export interface AcademicYearUsage {
  enrolments: number;
  feeStructures: number;
  attendanceRows: number;
  timetableEntries: number;
  classTeachers: number;
  homework: number;
  tests: number;
  events: number;
}

/** Status and is_current are never sent: they change only through toggle-status / set-current. */
export interface AcademicYearRequest {
  yearName: string;
  startDate: string;
  endDate: string;
}

/** A list row: display-ready text, plus raw dates for sorting. */
export interface AcademicYearRow extends Record<string, unknown> {
  academicYearId: number;
  yearName: string;
  startDate: string;
  endDate: string;
  currentLabel: string;
  statusName: string;
  isCurrent: boolean;
  isActive: boolean;
  isClosed: boolean;
  state: AcademicYearState;
  stateLabel: string;
  usageText: string;
}
