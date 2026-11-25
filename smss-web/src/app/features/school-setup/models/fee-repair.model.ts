/** "Assign fees for a year": enrolled students who have no fee structure get their class's active structure. */
export interface FeeRepairRequest {
  chargeFromYearStart: boolean;
  confirmBackdating: boolean;
  reason: string | null;
}

export interface FeeRepairNoStructure {
  className: string;
  students: number;
}

export interface FeeRepairPreview {
  academicYearId: number;
  yearName: string;
  /** Active enrolled students of the year with no fee structure. */
  missingCount: number;
  /** Of those, the ones a run would assign (their class has an active structure; at most maxPerRun). */
  wouldAssign: number;
  maxPerRun: number;
  /** The year's first day; yearStarted = it has already passed (then charging from it is back-dated). */
  yearStart: string;
  yearStarted: boolean;
  /** Back-dating is allowed only in the current academic year. */
  backdatingAllowed: boolean;
  classesWithoutStructure: FeeRepairNoStructure[];
}

export interface FeeRepairStudent {
  studentId: string;
  studentName: string;
  className: string;
  reason: string;
}

export interface FeeRepairResult {
  academicYearId: number;
  yearName: string;
  assigned: number;
  skippedNoStructure: number;
  remaining: number;
  backdated: boolean;
  failed: FeeRepairStudent[];
}
