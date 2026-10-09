// Mirrors SectionResponse / Create+UpdateSectionRequest.

export interface Section {
  sectionId: number;
  schoolId: string;
  classId: number;
  /** Joined by the server, so the list needs no second lookup. */
  className: string;
  /** The class's display order; used to group sections in class order. */
  classSequenceOrder: number;
  sectionName: string;
  maxStrength?: number | null;
  statusId?: number | null;
  /** "Active" / "Inactive", resolved by the server so the client never deals in status ids. */
  statusName?: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Status is never sent: it changes only through toggle-status. */
export interface SectionRequest {
  classId: number;
  sectionName: string;
  maxStrength: number | null;
}

/** A list row: display-ready values. */
export interface SectionRow extends Record<string, unknown> {
  sectionId: number;
  classSequenceOrder: number;
  className: string;
  sectionName: string;
  maxStrength: string;
  statusName: string;
  isActive: boolean;
}
