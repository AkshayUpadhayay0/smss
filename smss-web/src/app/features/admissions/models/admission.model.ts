import { ParentAccountResult } from '../../parent-account/models/parent-account.model';
import { FeeItemSelection } from '../../students/models/student-billing.model';

// Mirrors AdmissionResponseDto / AdmissionDetailDto / SubmitAdmissionRequestDto / ConfirmAdmissionRequestDto.

/**
 * The status CODES (unchanged in the database). Admission is two steps: an application is ADDED (APPLICATION_SUBMITTED) and then CONFIRMED (ADMITTED: the student is created);
 * REJECTED and CANCELLED (withdrawn) are exits. The old Under Review and Approved steps are gone; nothing is left in those states after the Stage 2c migration.
 */
export type AdmissionStatus = 'APPLICATION_SUBMITTED' | 'REJECTED' | 'ADMITTED' | 'CANCELLED';

/** Display label and pill colour per status. */
export const STATUS_META: Record<AdmissionStatus, { label: string; tone: string }> = {
  APPLICATION_SUBMITTED: { label: 'Application added', tone: 'blue' },
  REJECTED: { label: 'Rejected', tone: 'red' },
  ADMITTED: { label: 'Confirmed', tone: 'violet' },
  CANCELLED: { label: 'Cancelled', tone: 'gray-strike' },
};

export type AdmissionAction = 'reject' | 'cancel' | 'confirm';

/** Which workflow actions are valid in each status (mirrors the server's state machine). */
export const ACTIONS_BY_STATUS: Record<AdmissionStatus, AdmissionAction[]> = {
  APPLICATION_SUBMITTED: ['confirm', 'reject', 'cancel'],
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
  /** Optional: only the first name is mandatory. */
  applicantLastName?: string | null;
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
  /** With the country code; null on an application made before it was collected (it must be added before the application can be confirmed). */
  guardianWhatsAppNumber?: string | null;
  /** True when the consent tick was recorded on this application. */
  guardianConsentRecorded?: boolean;
  /** Only for a confirmed application: the student's primary guardian (for the Parent account card). */
  primaryGuardianId?: number | null;
  /** Set only on the response of a confirmation: what happened to the parent account (never a link or password). */
  parentAccount?: ParentAccountResult | null;
  /** The existing guardian this application reuses as the primary guardian (a sibling's parent), shown with a MASKED mobile number; null when the primary guardian was typed in. */
  guardianId?: number | null;
  existingGuardian?: { guardianId: number; name: string; relationship: string; maskedMobile: string; hasWhatsApp: boolean; hasEmail: boolean } | null;
  additionalGuardians?: { guardianId: number; name: string; relationship: string; maskedMobile: string; hasWhatsApp: boolean; hasEmail: boolean }[];
  previousSchool?: string | null;
  reviewNotes?: string | null;
  reviewedAt?: string | null;
  /** When the application was confirmed (the student was created). */
  confirmedAt?: string | null;
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
  applicantLastName: string | null;
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
  /** Mandatory: the primary guardian's email. */
  guardianEmail: string | null;
  /** The existing guardian reused as the PRIMARY guardian (a sibling's parent); null when the primary guardian is a person typed in. The name and mobile sent for them are ignored by the server. */
  existingGuardianId?: number | null;
  /** Other existing guardians to link (not primary), with the relationship for this student. */
  additionalGuardians?: { guardianId: number; relationship: string }[];
  /** The WhatsApp number as typed (10 digits or with the country code); null when "same as mobile number" is ticked. */
  guardianWhatsAppNumber: string | null;
  guardianWhatsAppSameAsMobile: boolean;
  /** The consent tick: the parent agrees to be contacted by WhatsApp and email. Required. */
  guardianConsent: boolean;
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
export const EDITABLE_STATUSES: readonly AdmissionStatus[] = ['APPLICATION_SUBMITTED'];
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
