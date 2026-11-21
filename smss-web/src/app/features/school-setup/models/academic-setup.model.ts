// Mirrors YearOfferingDto / SaveYearOfferingRequest (S2): which classes and sections a school offers in one academic year.

export interface YearOffering {
  academicYearId: number;
  yearName: string;
  isCurrent: boolean;
  isClosed: boolean;
  offeredClasses: number;
  offeredSections: number;
  classes: OfferingClass[];
}

export interface OfferingClass {
  classId: number;
  className: string;
  sequenceOrder: number;
  /** The class master is Active. An inactive class cannot be newly offered. */
  isActive: boolean;
  offered: boolean;
  enrolments: number;
  hasFeeStructure: boolean;
  sections: OfferingSection[];
}

export interface OfferingSection {
  sectionId: number;
  sectionName: string;
  isActive: boolean;
  offered: boolean;
  masterMaxStrength?: number | null;
  /** This year's own capacity; null = the section master's. */
  maxStrength?: number | null;
  effectiveMaxStrength?: number | null;
  enrolments: number;
  hasClassTeacher: boolean;
  timetableEntries: number;
}

export interface SaveYearOfferingRequest {
  classes: { classId: number; sections: { sectionId: number; maxStrength: number | null }[] }[];
}

/** What the screen edits: the same shape as the server's, with the capacity as text so a half-typed number is never lost. */
export interface ClassDraft {
  classId: number;
  className: string;
  isActive: boolean;
  offered: boolean;
  /** Why this class can't be switched off ("12 students are enrolled"), or null. */
  lockedReason: string | null;
  sections: SectionDraft[];
}

export interface SectionDraft {
  sectionId: number;
  sectionName: string;
  isActive: boolean;
  offered: boolean;
  capacity: string;
  masterCapacity: number | null;
  lockedReason: string | null;
}
