export interface TcDue { feeDueId: number; feeType: string; dueDate: string; amountDue: number; amountPaid: number; outstanding: number }

export interface TcCheck {
  studentId: string; studentName: string; admissionNumber: string; className: string | null; sectionName: string | null; yearName: string; outcome: string | null; leavingDate: string;
  canIssue: boolean; blockers: string[]; outstanding: number; dues: TcDue[]; requiresOverride: boolean; existingCertificateId: number | null; existingCertificateNumber: string | null;
  suggestedConduct: string; principalName: string | null;
}

export interface TcCopy { kind: string; copyNo: number; printedAt: string; printedBy: string | null }

export interface TransferCertificate {
  certificateId: number; certificateNumber: string; studentId: string; studentName: string; className: string | null; sectionName: string | null; outcome: string | null;
  status: 'ISSUED' | 'VOID'; conduct: string; remarks: string | null; feesCleared: boolean; overrideApplied: boolean; overrideReason: string | null; outstandingAmount: number;
  issuedAt: string; issuedBy: string | null; voidedAt: string | null; voidedBy: string | null; voidReason: string | null; duplicates: number; copies?: TcCopy[] | null;
}

/** The optional typed boxes live only in that certificate's snapshot (never on the student record) and are printed only when filled in. */
export interface IssueTcExtras {
  nationality?: string; religion?: string; category?: string; qualifiedForPromotion?: boolean; promotedToClass?: string; workingDays?: number; daysPresent?: number;
  feeConcession?: string; applicationDate?: string; boardConduct?: string;
}

export interface IssueTcRequest extends IssueTcExtras { studentId: string; conduct?: string; remarks?: string; confirm: boolean; overrideDues?: boolean; overrideReason?: string }
