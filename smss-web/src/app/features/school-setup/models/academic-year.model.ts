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
}
