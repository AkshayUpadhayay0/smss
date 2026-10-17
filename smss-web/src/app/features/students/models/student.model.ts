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
