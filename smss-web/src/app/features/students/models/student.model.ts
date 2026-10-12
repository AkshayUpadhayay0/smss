// Mirrors StudentListItemDto / StudentProfileDto / GuardianSummaryDto.

export interface StudentListItem {
  studentId: number;
  admissionNumber: string;
  fullName: string;
  /** From the enrollment, not the student. */
  rollNumber?: string | null;
  genderName?: string | null;
  statusName: string;
  enrollmentId: number;
  /** Relative public URL (/uploads/...); null = no photo. */
  profilePhotoUrl?: string | null;
}

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
  studentId: number;
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

  guardians: GuardianSummary[];
}

/** Table row for the list page. */
export interface StudentRow extends Record<string, unknown> {
  studentId: number;
  /** Absolute image URL, or '' (the avatar falls back to initials). */
  photoUrl: string;
  rollNumber: string;
  admissionNumber: string;
  fullName: string;
  genderName: string;
  statusName: string;
}

/** Mirrors StudentPhotoDto. */
export interface StudentPhoto {
  studentId: number;
  profilePhotoUrl: string | null;
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
