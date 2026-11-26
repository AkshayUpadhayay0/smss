// Mirrors GuardianDto / GuardianUpdateRequestDto / IncompleteGuardiansDto / BulkWhatsAppResultDto.

export interface GuardianStudent {
  studentId: string;
  studentName: string;
  isPrimary: boolean;
}

export interface Guardian {
  guardianId: number;
  name: string;
  relationship: string;
  mobileNumber: string;
  /** With the country code; null for a guardian added before it was collected. */
  whatsAppNumber: string | null;
  email: string | null;
  occupation: string | null;
  isActive: boolean;
  /** More than 1 means a change made here applies to all of those students. */
  linkedStudentCount: number;
  students: GuardianStudent[];
  /** The primary guardian of an active student: WhatsApp and email are required. */
  contactRequired: boolean;
}

export interface GuardianUpdateRequest {
  name: string;
  mobileNumber: string;
  /** As typed; null when "same as mobile number" is ticked (the server then uses the mobile number). */
  whatsAppNumber: string | null;
  whatsAppSameAsMobile: boolean;
  email: string | null;
  occupation: string | null;
}

export interface IncompleteGuardianStudent {
  studentId: string;
  studentName: string;
  classId: number | null;
  className: string | null;
  sectionId: number | null;
  sectionName: string | null;
}

export interface IncompleteGuardian {
  guardianId: number;
  name: string;
  relationship: string;
  mobileNumber: string;
  whatsAppNumber: string | null;
  email: string | null;
  /** "WhatsApp" and / or "Email". */
  missing: string[];
  students: IncompleteGuardianStudent[];
}

export interface IncompleteGuardians {
  /** Matching the filters (before paging). */
  total: number;
  /** All who need completing, before any filter (the dashboard tile's number). */
  totalIncomplete: number;
  page: number;
  pageSize: number;
  /** The academic year the class and section come from (the current-flagged year); null when the school has none. */
  viewedYearId: number | null;
  items: IncompleteGuardian[];
}

/** A parent shown by the sibling search: the name, a MASKED mobile number (never the whole number) and whether a WhatsApp number and an email are on record. */
export interface SiblingGuardian {
  guardianId: number;
  name: string;
  /** The relationship this guardian has to THAT student. */
  relationship: string;
  maskedMobile: string;
  hasWhatsApp: boolean;
  hasEmail: boolean;
  isPrimary: boolean;
}

export interface SiblingStudent {
  studentId: string;
  studentName: string;
  className: string | null;
  sectionName: string | null;
  guardians: SiblingGuardian[];
}

/** An existing guardian picked for the new student (as the forms hold them). */
export interface PickedGuardian {
  guardianId: number;
  name: string;
  relationship: string;
  maskedMobile: string;
  hasWhatsApp: boolean;
  hasEmail: boolean;
  /** When the parent consent is already on record (the form then shows it ticked, read-only). */
  consentGivenAt?: string | null;
}

/** The picked sibling's parents: the existing PRIMARY guardian (null when a new person typed in is primary) and the other existing guardians to link. */
export interface GuardianPicks {
  primary: PickedGuardian | null;
  extras: PickedGuardian[];
}

/** What the request carries for the pick: the existing primary guardian's id and the extra guardians with their relationship. Nothing about a stored number. */
export function picksToRequest(picks: GuardianPicks | null): { existingGuardianId: number | null; additionalGuardians: { guardianId: number; relationship: string }[] } {
  return {
    existingGuardianId: picks?.primary?.guardianId ?? null,
    additionalGuardians: (picks?.extras ?? []).map((g) => ({ guardianId: g.guardianId, relationship: g.relationship })),
  };
}

// ---- duplicate parents and merge (School Admin and Principal)
export interface DuplicateStudent { studentId: string; studentName: string; admissionNumber: string; className: string | null; sectionName: string | null; isPrimary: boolean }
export interface DuplicateGuardian {
  guardianId: number; name: string; relationship: string; mobileNumber: string; hasWhatsApp: boolean; hasEmail: boolean; hasLogin: boolean;
  createdAt: string;
  /** The oldest guardian of the group: the default survivor. */
  isOldest: boolean;
  students: DuplicateStudent[];
}
export interface DuplicateGroup {
  phoneKey: string;
  /** SAME / SIMILAR / DIFFERENT: how alike the names are (a shared phone alone is not proof of the same person). */
  nameMatch: 'SAME' | 'SIMILAR' | 'DIFFERENT';
  guardians: DuplicateGuardian[];
}
export interface DuplicateReport { groupCount: number; guardianCount: number; groups: DuplicateGroup[] }

export interface MergeRequest { survivorId: number; absorbedId: number; confirm: boolean; reason: string | null }
export interface MergeGuardianBrief { guardianId: number; name: string; relationship: string; mobileNumber: string; whatsAppNumber: string | null; email: string | null; occupation: string | null; hasLogin: boolean }
export interface MergeLink { studentId: string; studentName: string; admissionNumber: string; className: string | null; sectionName: string | null; relationship: string; isPrimary: boolean; action: 'MOVE' | 'COMBINE' }
export interface MergeApplication { admissionId: number; applicationNumber: string; role: 'PRIMARY' | 'EXTRA'; action: 'REPOINT' | 'DROP' }
export interface MergePreview {
  survivor: MergeGuardianBrief;
  absorbed: MergeGuardianBrief;
  links: MergeLink[];
  fieldsToCopy: Record<string, string>;
  applications: MergeApplication[];
  guardiansToRepoint: number[];
  account: 'NONE' | 'MOVE' | 'BLOCKED';
  canMerge: boolean;
  blockers: string[];
}
export interface MergeResult { mergeId: number; survivorId: number; absorbedId: number; linksMoved: number; linksCombined: number; applicationsRepointed: number; guardiansRepointed: number; fieldsCopied: string[]; accountMoved: boolean }

export interface BulkWhatsAppResult {
  selected: number;
  updated: number;
  skipped: number;
}
