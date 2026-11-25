// Mirrors StudentDocumentsDto / StudentRequiredDocumentDto / StudentDocumentDto / PendingStudentDocumentsDto.

/** PENDING is derived by the server (a required type with nothing yet): it is never stored. There is no OVERDUE. */
export type RequiredDocumentStatus = 'PENDING' | 'SUBMITTED' | 'WAIVED';

export interface StudentRequiredDocument {
  documentTypeId: number;
  documentTypeName: string;
  documentTypeCode: string;
  status: RequiredDocumentStatus;
  studentDocumentId: number | null;
}

export interface StudentDocument {
  studentDocumentId: number;
  documentTypeId: number;
  documentTypeName: string;
  status: 'SUBMITTED' | 'WAIVED';
  /** Absent for a Teacher: the class teacher sees statuses only. */
  fileName: string | null;
  contentType: string | null;
  fileSize: number | null;
  remarks: string | null;
  waiverReason: string | null;
  byName: string | null;
  at: string;
  fromApplication: boolean;
  voided: boolean;
}

export interface StudentDocuments {
  studentId: string;
  studentName: string;
  required: StudentRequiredDocument[];
  pendingCount: number;
  documents: StudentDocument[];
  /** Upload, waive and void: School Admin, Principal and Admission Staff. A Teacher only reads the statuses. */
  canManage: boolean;
}

export interface PendingStudentDocumentsRow {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  className: string | null;
  sectionName: string | null;
  missing: string[];
}

export interface PendingStudentDocuments {
  totalStudents: number;
  students: PendingStudentDocumentsRow[];
}

/** The label the chips show for a status. */
export const DOCUMENT_STATUS_LABEL: Record<RequiredDocumentStatus, string> = {
  PENDING: 'Document not submitted',
  SUBMITTED: 'Submitted',
  WAIVED: 'Waived',
};

export const STUDENT_DOCUMENT_ACCEPT = '.pdf,.png,.jpg,.jpeg,.webp';
export const STUDENT_DOCUMENT_MAX_BYTES = 5 * 1024 * 1024;
