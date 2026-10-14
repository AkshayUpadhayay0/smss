// Mirrors StudentFeesDto and its parts.

export interface StudentFeeLine {
  feeTypeId: number;
  feeTypeName: string;
  feeTypeCode: string;
  frequencyId: number;
  frequencyName: string;
  amount: number;
  /** What the applied concessions take off this line. */
  discount: number;
  netAmount: number;
}

export interface StudentFeeConcession {
  studentFeeConcessionId: number;
  concessionId: number;
  concessionName: string;
  concessionCode: string;
  concessionType: 'PERCENTAGE' | 'FIXED';
  value: number;
  /** Null = applies to every fee head. */
  applicableFeeTypeId?: number | null;
  applicableFeeTypeName?: string | null;
  remarks?: string | null;
  approvedByName?: string | null;
  appliedAt: string;
  /** False when it targets a fee head the current structure does not have (it takes nothing off). */
  isApplicable: boolean;
}

/** Per frequency: a monthly and an annual fee are never added together. */
export interface StudentFeeTotal {
  frequencyId: number;
  frequencyName: string;
  gross: number;
  concession: number;
  netPayable: number;
}

export interface StudentFeeStructureOption {
  feeStructureId: number;
  structureName: string;
  classId: number;
  className: string;
}

export interface StudentFeeConcessionOption {
  concessionId: number;
  concessionName: string;
  concessionCode: string;
  concessionType: 'PERCENTAGE' | 'FIXED';
  value: number;
  applicableFeeTypeId?: number | null;
  applicableFeeTypeName?: string | null;
}

export interface StudentFees {
  studentId: string;
  studentName: string;
  /** The enrollment the fees are for; null when the student has none. */
  academicYearId?: number | null;
  academicYearName?: string | null;
  classId?: number | null;
  className?: string | null;
  feeStructureId?: number | null;
  structureName?: string | null;
  structureIsActive: boolean;
  lines: StudentFeeLine[];
  concessions: StudentFeeConcession[];
  totals: StudentFeeTotal[];
  /** Active structures of the academic year (all classes), for assigning. */
  availableStructures: StudentFeeStructureOption[];
  /** Active concessions not yet applied that fit this structure. */
  availableConcessions: StudentFeeConcessionOption[];
}

/** Table row for the fee lines. */
export interface StudentFeeLineRow extends Record<string, unknown> {
  feeTypeName: string;
  frequencyName: string;
  amount: string;
  discount: string;
  netAmount: string;
}
