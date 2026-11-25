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

export interface BulkWhatsAppResult {
  selected: number;
  updated: number;
  skipped: number;
}
