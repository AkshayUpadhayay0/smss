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
