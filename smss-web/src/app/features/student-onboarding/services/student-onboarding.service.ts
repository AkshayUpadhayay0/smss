import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { FeeItemSelection } from '../../students/models/student-billing.model';

const BASE = '/api/StudentOnboarding';

/** Mirrors DirectStudentEntryRequestDto. Dates are yyyy-MM-dd. */
export interface DirectStudentEntryRequest {
  admissionNumber: string | null;
  admissionDate: string;
  academicYearId: number;
  /** tb_sections.section_id */
  classSectionId: number;
  rollNumber: string | null;
  firstName: string;
  middleName: string | null;
  /** Optional: only the first name is mandatory. */
  lastName: string | null;
  dateOfBirth: string;
  genderId: number | null;
  bloodGroupId: number | null;
  studentCategoryId: number | null;
  aadharNumber: string | null;
  email: string | null;
  mobileNumber: string | null;
  guardianName: string;
  guardianRelationship: string;
  guardianMobileNumber: string;
  guardianEmail: string | null;
  /** The existing guardian reused as the PRIMARY guardian (a sibling's parent); null when the primary guardian is a person typed in. */
  existingGuardianId?: number | null;
  /** Other existing guardians to link (not primary), with the relationship for this student. */
  additionalGuardians?: { guardianId: number; relationship: string }[];
  /** The WhatsApp number as typed; null when "same as mobile number" is ticked (the server then uses the mobile number). */
  guardianWhatsAppNumber: string | null;
  guardianWhatsAppSameAsMobile: boolean;
  /** The consent tick (required). */
  guardianConsent: boolean;
  /** Fee configuration chosen at admission (installment plan per fee head). Omitted = the fee structure's defaults. */
  feeItems?: FeeItemSelection[] | null;
  /** The date fees are charged from (yyyy-MM-dd); omitted = today. An earlier date needs confirmBackdating and a reason. */
  chargeFrom?: string | null;
  confirmBackdating?: boolean;
  backdatingReason?: string | null;
}

/** Mirrors StudentOnboardingResponseDto. The Aadhaar comes back already masked. */
export interface StudentOnboardingResult {
  /** Business id string, e.g. DPS001ST001. */
  studentId: string;
  admissionNumber: string;
  fullName: string;
  enrollmentId: number;
  aadharNumberMasked?: string | null;
  /** The fee structure the student was automatically assigned to, when one matched. */
  feeStructureName?: string | null;
  /** Things that did not block the operation (e.g. no fee structure found for the class). */
  warnings?: string[];
}

/**
 * The route carries the school id. It always comes from the signed-in session, and the server refuses any id
 * that isn't the caller's own school. Failures are handled by the page (it needs to recognise the
 * "Existing Student admission type missing" case), so the global error toast is skipped.
 */
@Injectable({ providedIn: 'root' })
export class StudentOnboardingService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  createExistingStudent(payload: DirectStudentEntryRequest): Observable<StudentOnboardingResult> {
    const schoolId = this.auth.user()?.schoolId;
    if (!schoolId) return throwError(() => new Error('Your account is not linked to a school.'));
    return this.api.post<StudentOnboardingResult>(`${BASE}/${encodeURIComponent(schoolId)}/existing-student`, payload, { skipErrorToast: true });
  }
}
