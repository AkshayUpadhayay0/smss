// Mirrors ClassResponse / Create+UpdateClassRequest.

export interface SchoolClass {
  classId: number;
  schoolId: string;
  className: string;
  classCode?: string | null;
  sequenceOrder: number;
  statusId?: number | null;
  /** "Active" / "Inactive", resolved by the server so the client never deals in status ids. */
  statusName?: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Status is never sent: it changes only through toggle-status. */
export interface ClassRequest {
  className: string;
  classCode: string | null;
  sequenceOrder: number;
}

/** A list row: display-ready values. */
export interface ClassRow extends Record<string, unknown> {
  classId: number;
  className: string;
  classCode: string;
  sequenceOrder: number;
  statusName: string;
  isActive: boolean;
}
