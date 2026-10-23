import { FeeItemSelection } from '../../students/models/student-billing.model';

// Mirrors AdmissionResponseDto / AdmissionDetailDto / SubmitAdmissionRequestDto / ConfirmAdmissionRequestDto.

export type AdmissionStatus = 'APPLICATION_SUBMITTED' | 'UNDER_REVIEW' | 'APPROVED' | 'REJECTED' | 'ADMITTED' | 'CANCELLED';

/** Display label and pill colour per status. */
export const STATUS_META: Record<AdmissionStatus, { label: string; tone: string }> = {
  APPLICATION_SUBMITTED: { label: 'Submitted', tone: 'gray' },
  UNDER_REVIEW: { label: 'Under Review', tone: 'blue' },
  APPROVED: { label: 'Approved', tone: 'green' },
  REJECTED: { label: 'Rejected', tone: 'red' },
  ADMITTED: { label: 'Admitted', tone: 'violet' },
  CANCELLED: { label: 'Cancelled', tone: 'gray-strike' },
};

export type AdmissionAction = 'start-review' | 'approve' | 'reject' | 'cancel' | 'confirm';

/** Which workflow actions are valid in each status (mirrors the server's state machine). */
export const ACTIONS_BY_STATUS: Record<AdmissionStatus, AdmissionAction[]> = {
  APPLICATION_SUBMITTED: ['start-review', 'cancel'],
  UNDER_REVIEW: ['approve', 'reject', 'cancel'],
  APPROVED: ['confirm'],
  REJECTED: [],
  ADMITTED: [],
  CANCELLED: [],
};

export const GUARDIAN_RELATIONSHIPS = [
  { label: 'Father', value: 'FATHER' },
  { label: 'Mother', value: 'MOTHER' },
  { label: 'Guardian', value: 'GUARDIAN' },
] as const;

export interface Admission {
  admissionId: number;
  applicationNumber: string;
  applicantFullName: string;
  statusName: AdmissionStatus;
  /** Already masked by the server (XXXX XXXX 1234); never unmask client-side. */
  aadharNumberMasked?: string | null;
  applicationDate: string;
  /** Business id string (e.g. DPS001ST001), set once the application is ADMITTED. */
  studentId?: string | null;
  enrollmentId?: number | null;
  academicYearId: number;
  academicYearName?: string | null;
  appliedClassId: number;
  appliedClassName?: string | null;
  admissionTypeId: number;
  admissionTypeName?: string | null;
}

export interface AdmissionDetail extends Admission {
  /** Set by confirm-admission: the fee structure the student was automatically assigned to. */
  feeStructureName?: string | null;
  /** Things that did not block the operation (e.g. no fee structure found for the class). */
  warnings?: string[];
  applicantFirstName: string;
  applicantMiddleName?: string | null;
  applicantLastName: string;
  dateOfBirth: string;
  genderId?: number | null;
  genderName?: string | null;
  bloodGroupId?: number | null;
  bloodGroupName?: string | null;
  studentCategoryId?: number | null;
  studentCategoryName?: string | null;
  studentNationId?: string | null;
  email?: string | null;
  mobileNumber?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  countryId?: number | null;
  stateId?: number | null;
  districtId?: number | null;
  cityId?: number | null;
  pincode?: string | null;
  guardianName: string;
  guardianRelationship: string;
  guardianMobileNumber: string;
  guardianEmail?: string | null;
  previousSchool?: string | null;
  reviewNotes?: string | null;
  reviewedAt?: string | null;
  approvedAt?: string | null;
  rejectedAt?: string | null;
  decisionRemarks?: string | null;
  rejectionReason?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SubmitAdmissionRequest {
  academicYearId: number;
  appliedClassId: number;
  admissionTypeId: number;
  applicantFirstName: string;
  applicantMiddleName: string | null;
  applicantLastName: string;
  dateOfBirth: string; // yyyy-MM-dd
  genderId: number | null;
  bloodGroupId: number | null;
  studentCategoryId: number | null;
  aadharNumber: string | null;
  studentNationId: string | null;
  email: string | null;
  mobileNumber: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  countryId: number | null;
  stateId: number | null;
  districtId: number | null;
  cityId: number | null;
  pincode: string | null;
  guardianName: string;
  guardianRelationship: string;
  guardianMobileNumber: string;
  guardianEmail: string | null;
  previousSchool: string | null;
}

export interface ConfirmAdmissionRequest {
  /** tb_sections.section_id */
  classSectionId: number;
  rollNumber: string | null;
  /** Fee configuration chosen at admission (frequency per fee head). Omitted = the fee structure's defaults. */
  feeItems?: FeeItemSelection[] | null;
}

/** Mirrors AdmissionDocumentDto (the file itself is only ever fetched through the authenticated download). */
export interface AdmissionDocument {
  applicationDocumentId: number;
  documentTypeId: number;
  documentTypeName: string;
  fileName: string;
  contentType: string;
  fileSize: number;
  remarks?: string | null;
  uploadedByName?: string | null;
  uploadedAt: string;
  statusName: string;
}

/** Statuses in which the applicant details can still be edited (the server enforces it too). */
export const EDITABLE_STATUSES: readonly AdmissionStatus[] = ['APPLICATION_SUBMITTED', 'UNDER_REVIEW'];
/** Statuses after which documents can no longer be added or removed. */
export const DOCUMENT_LOCKED_STATUSES: readonly AdmissionStatus[] = ['REJECTED', 'CANCELLED', 'ADMITTED'];

export interface AdmissionFilters {
  status?: AdmissionStatus | '';
  classId?: number;
  academicYearId?: number;
}

export const formatDate = (iso?: string | null): string => {
  if (!iso) return '';
  const d = new Date(`${iso.slice(0, 10)}T00:00:00`);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
};
