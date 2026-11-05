// Mirrors StudentListItemDto / StudentProfileDto / GuardianSummaryDto.
// studentId is the business id string (e.g. DPS001ST001), never a number.

export interface StudentListItem {
  studentId: string;
  admissionNumber: string;
  fullName: string;
  /** From the enrollment, not the student. */
  rollNumber?: string | null;
  genderName?: string | null;
  statusName: string;
  enrollmentId: number;
  /** Relative public URL (/uploads/...); null = no photo. */
  profilePhotoUrl?: string | null;
  /** Overdue | Partially Paid | Pending | Paid | Not Set (no fee installments for the list's academic year). */
  feeStatus: FeeStatus;
  /** Totals over the year's installments; null/absent when Not Set. */
  feeTotalDue?: number | null;
  feeTotalPaid?: number | null;
  feeOutstanding?: number | null;
}

export type FeeStatus = 'Overdue' | 'Partially Paid' | 'Pending' | 'Paid' | 'Not Set';

export interface GuardianSummary {
  guardianId: number;
  fullName: string;
  relationship: string;
  mobileNumber: string;
  email?: string | null;
  isPrimary: boolean;
  isEmergencyContact: boolean;
  canPickupStudent: boolean;
}

export interface StudentProfile {
  /** True when the API returned the limited view (an Accountant): header + class/section only, everything else empty. */
  limited?: boolean;
  studentId: string;
  admissionNumber: string;
  fullName: string;
  dateOfBirth: string;
  genderName?: string | null;
  bloodGroupName?: string | null;
  studentCategoryName?: string | null;
  admissionTypeName?: string | null;
  admissionDate: string;
  email?: string | null;
  mobileNumber?: string | null;
  statusName: string;
  /** Already masked by the server (XXXX XXXX 1234); never unmask client-side. */
  aadharNumberMasked?: string | null;
  /** Relative public URL (/uploads/...); null = no photo. */
  profilePhotoUrl?: string | null;

  // Current enrollment (null when the student has none yet)
  enrollmentId?: number | null;
  academicYearName?: string | null;
  className?: string | null;
  sectionName?: string | null;
  rollNumber?: string | null;
  enrollmentStatusName?: string | null;

  /** The student's login id once a login exists; null before. The password is never returned again. */
  loginId?: string | null;

  guardians: GuardianSummary[];
}

/** Table row for the list page. */
export interface StudentRow extends Record<string, unknown> {
  studentId: string;
  /** Absolute image URL, or '' (the avatar falls back to initials). */
  photoUrl: string;
  rollNumber: string;
  admissionNumber: string;
  fullName: string;
  genderName: string;
  statusName: string;
  feeStatus: string;
}

/** Mirrors StudentPhotoDto. */
export interface StudentPhoto {
  studentId: string;
  profilePhotoUrl: string | null;
}

/** Mirrors StudentLoginCreatedDto — returned once, right after the login is created. */
export interface StudentLoginCreated {
  studentId: string;
  displayName: string;
  /** The Login ID to sign in with (e.g. DPS001ST001). */
  orgUserId: string;
  /** Shown once; the server cannot return it again. */
  temporaryPassword: string;
  emailSent: boolean;
  emailSentTo?: string | null;
}

/** Table row for the guardians list on the profile page. */
export interface GuardianRow extends Record<string, unknown> {
  guardianId: number;
  fullName: string;
  relationship: string;
  mobileNumber: string;
  email: string;
  primaryLabel: string;
  emergencyLabel: string;
  pickupLabel: string;
}

// ---- GET /api/Student/my-class (a Teacher's own students). Mirrors MyClassDtos.cs. No fee, login, contact or Aadhaar fields exist on these.
export interface MyClassStudent {
  studentId: string;
  admissionNumber: string;
  fullName: string;
  rollNumber?: string | null;
  genderName?: string | null;
  statusName: string;
  /** Relative public URL (/uploads/...); null = no photo. */
  profilePhotoUrl?: string | null;
}

export interface MyClassSection {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  /** The teacher is the section's Active class teacher (listed first). */
  isClassTeacher: boolean;
  /** Subjects the teacher is timetabled to teach in this section; empty when they are only its class teacher. */
  subjects: string[];
  students: MyClassStudent[];
}

export interface MyClass {
  academicYearId: number;
  academicYearName: string;
  sections: MyClassSection[];
}

/** Table row of the My Students page. */
export interface MyClassStudentRow extends Record<string, unknown> {
  studentId: string;
  photoUrl: string;
  rollNumber: string;
  admissionNumber: string;
  fullName: string;
  genderName: string;
  statusName: string;
}

/** Mirrors IdCardStudentRefDto: a student named in the bulk ID-card report (`reason` only on excluded ones). */
export interface IdCardStudentRef {
  studentId: string;
  admissionNumber: string;
  fullName: string;
  rollNumber: string | null;
  reason: string | null;
}

/** Mirrors IdCardBulkResultDto. `pdfBase64` is null when no student of the section qualified. */
export interface IdCardBulkResult {
  fileName: string;
  contentType: string;
  pdfBase64: string | null;
  cardCount: number;
  sheetCount: number;
  maxCardsPerRequest: number;
  academicYearName: string;
  /** Printed with the initials placeholder: no (readable) photo. */
  noPhoto: IdCardStudentRef[];
  /** Left out of the PDF, each with the reason. */
  excluded: IdCardStudentRef[];
}

/** Mirrors MyGuardianDto: a guardian as the student / parent sees them. */
export interface MyGuardian {
  fullName: string;
  relationship: string;
  mobileNumber?: string | null;
  email?: string | null;
}

/** Mirrors StudentMyProfileDto (GET /api/Student/me): only what a student / parent should see (no Aadhaar, login id, fees or internal fields). */
export interface StudentMyProfile {
  studentId: string;
  fullName: string;
  admissionNumber: string;
  statusName: string;
  profilePhotoUrl?: string | null;
  academicYearName?: string | null;
  className?: string | null;
  sectionName?: string | null;
  rollNumber?: string | null;
  genderName?: string | null;
  dateOfBirth: string;
  bloodGroupName?: string | null;
  studentCategoryName?: string | null;
  mobileNumber?: string | null;
  email?: string | null;
  address?: string | null;
  guardians: MyGuardian[];
}
