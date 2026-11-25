// Mirrors PromotionPreviewRequest / PromotionPreviewDto (4b-2: the preview only; nothing is written).

export type PromotionDecision = 'PROMOTE' | 'REPEAT' | 'PASS_OUT' | 'LEAVE';
export type PromotionRowStatus = 'READY' | 'BLOCKED' | 'NEEDS_ATTENTION' | 'NO_DECISION' | 'ALREADY_ENROLLED' | 'ALREADY_RECORDED';

export const DECISION_LABELS: Record<PromotionDecision, string> = { PROMOTE: 'Promote', REPEAT: 'Repeat', PASS_OUT: 'Pass out', LEAVE: 'Leave' };

export const STATUS_LABELS: Record<PromotionRowStatus, string> = {
  READY: 'Ready',
  BLOCKED: 'Blocked: dues',
  NEEDS_ATTENTION: 'Needs attention',
  NO_DECISION: 'No decision yet',
  ALREADY_ENROLLED: 'Already enrolled',
  ALREADY_RECORDED: 'Already recorded',
};

/** An edit the admin made on a row; anything not listed keeps its prefilled value. */
export interface PromotionDecisionInput {
  studentId: string;
  decision?: PromotionDecision | null;
  toClassId?: number | null;
  toSectionId?: number | null;
  rollNumber?: string | null;
}

export interface PromotionPreviewRequest {
  sourceYearId: number;
  targetYearId: number;
  classId: number;
  sectionId: number | null;
  promotionDate: string | null;
  decisions: PromotionDecisionInput[];
}

export interface PromotionTarget {
  classId: number;
  className: string;
  sectionId: number | null;
  sectionName: string | null;
}

export interface PromotionDue {
  feeDueId: number;
  feeType: string;
  dueDate: string;
  amountDue: number;
  amountPaid: number;
  outstanding: number;
}

export interface PromotionRow {
  studentId: string;
  admissionNumber: string;
  studentName: string;
  sourceEnrollmentId: number;
  sourceSectionId: number;
  sourceSectionName: string;
  sourceRollNumber: string | null;
  teacherDecision: string | null;
  decision: PromotionDecision | null;
  decisionSource: 'TEACHER' | 'ADMIN' | 'NONE';
  target: PromotionTarget | null;
  promoteDefault: PromotionTarget | null;
  repeatDefault: PromotionTarget | null;
  rollNumber: string | null;
  status: PromotionRowStatus;
  blockedByDues: boolean;
  outstanding: number;
  notYetDue: number;
  dues: PromotionDue[];
  issues: string[];
  notes: string[];
}

export interface PromotionSectionOption {
  sectionId: number;
  sectionName: string;
  maxStrength: number | null;
  enrolled: number;
}

export interface PromotionClassOption {
  classId: number;
  className: string;
  hasFeeStructure: boolean;
  sections: PromotionSectionOption[];
}

export interface PromotionCounts {
  total: number;
  ready: number;
  blocked: number;
  needsAttention: number;
  noDecision: number;
  alreadyEnrolled: number;
  alreadyRecorded: number;
}

export interface PromotionPreview {
  sourceYearId: number;
  sourceYearName: string;
  sourceYearClosed: boolean;
  targetYearId: number;
  targetYearName: string;
  /** The target year's first day; targetYearStarted = it has passed, so a run charges fees from it (needs the back-dating confirmation and a reason). */
  targetYearStart: string;
  targetYearStarted: boolean;
  scope: 'CLASS' | 'SECTION';
  classId: number;
  className: string;
  sectionId: number | null;
  sectionName: string | null;
  promotionDate: string;
  nextClassId: number | null;
  nextClassName: string | null;
  isLastClass: boolean;
  maxPerRun: number;
  counts: PromotionCounts;
  classesWithoutFeeStructure: string[];
  needsFeeAcknowledgement: boolean;
  targetOptions: PromotionClassOption[];
  rows: PromotionRow[];
}

/** The preview request plus what a run adds. The server works everything else out again. */
export interface PromotionExecuteRequest extends PromotionPreviewRequest {
  confirm: boolean;
  overrideReason: string | null;
  overrideStudentIds: string[];
  acknowledgeNoFeeStructure: boolean;
  confirmBackdating: boolean;
}

export type PromotionItemOutcome = 'DONE' | 'OVERRIDDEN' | 'SKIPPED';

export interface PromotionResultItem {
  studentId: string;
  studentName: string;
  decision: PromotionDecision | null;
  outcome: PromotionItemOutcome;
  reason: string | null;
  target: PromotionTarget | null;
  rollNumber: string | null;
  outstanding: number;
}

export interface PromotionFeeFailure {
  studentId: string;
  studentName: string;
  reason: string;
}

export interface PromotionResult {
  batchId: number | null;
  promoted: number;
  repeated: number;
  passedOut: number;
  left: number;
  overridden: number;
  skipped: number;
  leftOut: number;
  feesAssigned: number;
  feesNoStructure: number;
  backdated: boolean;
  items: PromotionResultItem[];
  feeFailures: PromotionFeeFailure[];
}

export interface PromotionPendingRow {
  studentId: string;
  admissionNumber: string;
  studentName: string;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  rollNumber: string | null;
  teacherDecision: string | null;
  outstanding: number;
  status: 'BLOCKED' | 'NO_DECISION';
}

export interface PromotionPending {
  hasEarlierYear: boolean;
  currentYearName: string | null;
  earlierYearName: string | null;
  total: number;
  blocked: number;
  rows: PromotionPendingRow[];
}

// ---- the correction of ONE promotion (4b-4)
export interface PromotionCorrectionRemoval {
  newYearName: string | null;
  className: string | null;
  sectionName: string | null;
  rollNumber: string | null;
  enrolment: boolean;
  feeItems: number;
  dues: number;
  duesAmount: number;
  feeStructure: boolean;
}

export interface PromotionCorrectionCheck {
  studentId: string;
  studentName: string;
  sourceYearName: string;
  decision: PromotionDecision | null;
  outcome: string | null;
  canCorrect: boolean;
  blockers: string[];
  willRemove: PromotionCorrectionRemoval;
}

export interface PromotionCorrectionResult {
  correctionId: number;
  studentId: string;
  outcomeCleared: string;
  removed: PromotionCorrectionRemoval;
}

