// Mirrors SectionFeeStatusDto (GET /api/Fee/section-status): a status and three amounts per student, nothing else.

export interface SectionFeeSummary {
  students: number;
  fullyPaid: number;
  partiallyPaid: number;
  unpaid: number;
  overdue: number;
  /** No fee structure assigned: not counted in the four above. */
  notSet: number;
}

export interface SectionFeeStudent {
  rollNumber?: string | null;
  fullName: string;
  /** Paid / Partially Paid / Pending / Overdue / Not Set */
  status: string;
  totalDue: number;
  totalPaid: number;
  balance: number;
}

export interface SectionFeeStatus {
  sectionId: number;
  className: string;
  sectionName: string;
  academicYearId: number;
  academicYearName: string;
  summary: SectionFeeSummary;
  students: SectionFeeStudent[];
}
