export interface LeavingDue { feeDueId: number; feeType: string; dueDate: string; amountDue: number; amountPaid: number; outstanding: number }

export interface LeavingCheck {
  studentId: string; studentName: string; admissionNumber: string; className: string | null; sectionName: string | null; yearName: string; enrollmentDate: string; leavingDate: string;
  canRecord: boolean; blockers: string[]; outstanding: number; dues: LeavingDue[]; notYetDue: number; requiresOverride: boolean;
}

export type LeavingType = 'LEFT' | 'TRANSFERRED_OUT';

export interface RecordLeavingRequest { studentId: string; type: LeavingType; leavingDate?: string; reason: string; confirm: boolean; overrideDues?: boolean; overrideReason?: string }
export interface RecordLeavingResult { studentId: string; studentName: string; outcome: string; leavingDate: string; outstanding: number; overrideApplied: boolean }

export interface LeavingCorrectionCheck { studentId: string; studentName: string; yearName: string; outcome: string | null; leavingDate: string | null; canCorrect: boolean; blockers: string[] }

export interface Leaver {
  studentId: string; studentName: string; admissionNumber: string; className: string | null; sectionName: string | null; outcome: string; leavingDate: string | null; reason: string | null;
  outstanding: number | null; overridden: boolean; recordedBy: string | null; yearName: string;
}

export interface SectionTarget { sectionId: number; sectionName: string; maxStrength: number | null; enrolled: number; isFull: boolean; nextRoll: string }

export interface SectionChangeCheck {
  studentId: string; studentName: string; admissionNumber: string; className: string; yearName: string; currentSectionId: number; currentSectionName: string; currentRoll: string | null;
  canChange: boolean; blockers: string[]; targets: SectionTarget[];
}

export interface ChangeSectionRequest { studentId: string; toSectionId: number; effectiveDate?: string; reason: string; rollNumber?: string; confirm: boolean }
export interface SectionChangeResult { studentId: string; studentName: string; fromSection: string; toSection: string; fromRoll: string | null; toRoll: string | null; effectiveDate: string }

export interface SectionChangeHistory {
  changeId: number; yearName: string; fromSection: string; toSection: string; fromRoll: string | null; toRoll: string | null; effectiveDate: string; reason: string; changedBy: string | null; changedAt: string;
}
