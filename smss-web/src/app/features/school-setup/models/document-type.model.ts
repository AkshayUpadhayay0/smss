// Mirrors DocumentTypeResponseDto / Create+UpdateDocumentTypeRequestDto.

export type AppliesTo = 'STUDENT' | 'EMPLOYEE' | 'BOTH';

export const APPLIES_TO_LABELS: Record<AppliesTo, string> = { STUDENT: 'Student', EMPLOYEE: 'Employee', BOTH: 'Both' };

export interface DocumentType {
  documentTypeId: number;
  schoolId: string;
  documentName: string;
  documentCode: string;
  appliesTo: AppliesTo;
  isRequired: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

/** Status is never sent: it changes only through toggle-status. */
export interface DocumentTypeRequest {
  documentName: string;
  documentCode: string;
  appliesTo: AppliesTo;
  isRequired: boolean;
}

/** A list row: display-ready values. */
export interface DocumentTypeRow extends Record<string, unknown> {
  documentTypeId: number;
  documentName: string;
  documentCode: string;
  appliesToLabel: string;
  requiredLabel: string;
  statusName: string;
  isActive: boolean;
}
