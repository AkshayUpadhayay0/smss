// Mirrors FeeStructureSummaryResponse / FeeStructureResponse / Create+UpdateFeeStructureRequest / FeeFrequencyResponse.

/** A list row: the header plus how many fee heads it has. */
export interface FeeStructureSummary {
  feeStructureId: number;
  schoolId: string;
  academicYearId: number;
  academicYearName: string;
  classId: number;
  className: string;
  structureName: string;
  isActive: boolean;
  /** "Active" / "Inactive". */
  statusName: string;
  lineCount: number;
  createdAt: string;
  updatedAt: string;
}

/** One fee head of a structure, with the names already joined by the server. */
export interface FeeStructureLine {
  feeStructureDetailId: number;
  feeTypeId: number;
  feeTypeName: string;
  feeTypeCode: string;
  frequencyId: number;
  frequencyName: string;
  amount: number;
  /** Day of the month a recurring fee falls due (1-31); null for One Time / Annual. */
  dueDay?: number | null;
  /** From when the line is charged (yyyy-MM-dd). */
  effectiveFrom: string;
  /** Students already have dues for it: its frequency can no longer be changed. */
  hasDues?: boolean;
}

/** The header together with its lines (GET by id). */
export interface FeeStructure extends FeeStructureSummary {
  /** Any student is assigned: a new line then defaults to starting today. */
  hasAssignedStudents?: boolean;
  details: FeeStructureLine[];
}

export interface FeeStructureLineRequest {
  feeTypeId: number;
  frequencyId: number;
  amount: number;
  /** Required for Monthly / Quarterly / Half-Yearly; null otherwise. */
  dueDay: number | null;
  /** yyyy-MM-dd, inside the structure's academic year. */
  effectiveFrom: string;
}

export interface CreateFeeStructureRequest {
  academicYearId: number;
  classId: number;
  structureName: string;
  details: FeeStructureLineRequest[];
}

/** Academic year and class are fixed once a structure exists, so they are not part of an update. */
export interface UpdateFeeStructureRequest {
  structureName: string;
  details: FeeStructureLineRequest[];
  /** Sent as true after the person confirmed that unpaid dues before a later effective date are removed. */
  confirmDueRemoval?: boolean;
  /** Sent as true after the person confirmed that removing a fee line deletes the unpaid dues of its students (see LineRemovalConfirmation). */
  confirmLineRemoval?: boolean;
}

/** The 409 answer of an update that needs that confirmation. */
export interface DueRemovalConfirmation {
  confirmationRequired: boolean;
  duesCount: number;
  totalAmount: number;
  /** Present (LINE_REMOVAL) when the confirmation is for removing fee lines rather than for moving a date. */
  kind?: 'LINE_REMOVAL';
  lines?: LineRemovalImpact[];
  pastCount?: number;
  pastAmount?: number;
  futureCount?: number;
  futureAmount?: number;
  itemsDeactivated?: number;
  itemsRemoved?: number;
}

/** What removing one fee line would do to the students already on the structure. */
export interface LineRemovalImpact {
  feeTypeId: number;
  feeTypeName: string;
  items: number;
  itemsDeactivated: number;
  itemsRemoved: number;
  pastCount: number;
  pastAmount: number;
  futureCount: number;
  futureAmount: number;
  paidDuesKept: number;
}

export interface BackdatingStudent {
  studentId: string;
  studentName: string;
  duesCount: number;
  amount: number;
}

/** What "Apply to earlier months" would create (nothing is written by a preview). */
export interface BackdatingPreview {
  feeStructureId: number;
  feeTypeId: number;
  feeTypeName: string;
  fromDate: string;
  toDate: string;
  currentEffectiveFrom: string;
  studentsCount: number;
  duesCount: number;
  totalAmount: number;
  students: BackdatingStudent[];
}

export interface BackdatingResult {
  backdatingLogId?: number | null;
  studentsCount: number;
  duesCount: number;
  totalAmount: number;
  effectiveFrom: string;
}

/** lut_fee_frequency (global): One Time, Monthly, Quarterly, Half-Yearly, Annual. */
export interface FeeFrequency {
  frequencyId: number;
  frequencyName: string;
  frequencyCode: string;
}

/** A fee type as the Fee Types master returns it (only what the builder needs). */
export interface FeeTypeOption {
  feeTypeId: number;
  feeTypeName: string;
  feeTypeCode: string;
  isActive: boolean;
}

/** List table row. */
export interface FeeStructureRow extends Record<string, unknown> {
  feeStructureId: number;
  structureName: string;
  className: string;
  academicYearName: string;
  lineCount: number;
  statusName: string;
  isActive: boolean;
}
