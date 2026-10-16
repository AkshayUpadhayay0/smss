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
}

/** The header together with its lines (GET by id). */
export interface FeeStructure extends FeeStructureSummary {
  details: FeeStructureLine[];
}

export interface FeeStructureLineRequest {
  feeTypeId: number;
  frequencyId: number;
  amount: number;
  /** Required for Monthly / Quarterly / Half-Yearly; null otherwise. */
  dueDay: number | null;
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
