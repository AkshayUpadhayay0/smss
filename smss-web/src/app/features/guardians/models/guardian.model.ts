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

export interface BulkWhatsAppResult {
  selected: number;
  updated: number;
  skipped: number;
}
