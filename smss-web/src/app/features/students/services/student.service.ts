import { Injectable, inject } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { FeeReceipt, PaymentSummary, RecordPaymentRequest } from '../models/fee-payment.model';
import { StudentFees } from '../models/student-fees.model';
import { FeePreview, StudentFeeDues, StudentFeeItems, UpdateFeeItemRequest } from '../models/student-billing.model';
import { StudentListItem, StudentLoginCreated, StudentPhoto, StudentProfile } from '../models/student.model';

const BASE = '/api/Student';

/**
 * The routes carry the school id. It always comes from the signed-in session, and the server refuses any id
 * that isn't the caller's own school.
 */
@Injectable({ providedIn: 'root' })
export class StudentService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  /** Students enrolled in a section (a tb_sections id) for an academic year. */
  list(academicYearId: number, classSectionId: number, feeStatus?: string): Observable<StudentListItem[]> {
    const params: Record<string, string | number> = { academicYearId, classSectionId };
    if (feeStatus && feeStatus !== 'All') params['feeStatus'] = feeStatus; // filtered on the server
    return this.withSchool((s) => this.api.get<StudentListItem[]>(`${BASE}/${s}`, params)).pipe(map((l) => l ?? []));
  }

  getProfile(studentId: string): Observable<StudentProfile> {
    return this.withSchool((s) => this.api.get<StudentProfile>(`${BASE}/${s}/${encodeURIComponent(studentId)}`));
  }

  /**
   * Same two-step pattern as the school logo: the student is created first, then the photo is sent on its own.
   * Callers handle a failure themselves (the student is already saved), so no global error toast.
   */
  uploadPhoto(studentId: string, file: File): Observable<StudentPhoto> {
    return this.withSchool((s) => this.api.upload<StudentPhoto>(`${BASE}/${s}/${encodeURIComponent(studentId)}/photo`, file, 'file', { skipErrorToast: true }));
  }

  removePhoto(studentId: string): Observable<StudentPhoto> {
    return this.withSchool((s) => this.api.post<StudentPhoto>(`${BASE}/${s}/${encodeURIComponent(studentId)}/photo/remove`, {}, { skipErrorToast: true }));
  }

  /** Creates the student's login; the response carries the generated Login ID and temporary password (shown once). */
  createLogin(studentId: string): Observable<StudentLoginCreated> {
    return this.withSchool((s) => this.api.post<StudentLoginCreated>(`${BASE}/${s}/${encodeURIComponent(studentId)}/create-login`, {}));
  }

  // ---- fee items and installments (the server takes the school from the token)
  /** The fee heads a new student of this class + academic year would get, for the admission forms (nothing is created). */
  previewFees(academicYearId: number, classId: number, startDate?: string | null): Observable<FeePreview> {
    return this.api.get<FeePreview>('/api/StudentFee/preview', { academicYearId, classId, ...(startDate ? { startDate } : {}) });
  }

  getFeeItems(studentId: string): Observable<StudentFeeItems> {
    return this.api.get<StudentFeeItems>(`/api/StudentFee/${encodeURIComponent(studentId)}/fee-items`);
  }

  /** Change the frequency a student pays a fee head at, and/or whether it applies to them; unpaid future installments are regenerated. */
  updateFeeItem(studentId: string, itemId: number, request: UpdateFeeItemRequest): Observable<StudentFeeItems> {
    return this.api.put<StudentFeeItems>(`/api/StudentFee/${encodeURIComponent(studentId)}/fee-items/${itemId}`, request);
  }

  getFeeDues(studentId: string): Observable<StudentFeeDues> {
    return this.api.get<StudentFeeDues>(`/api/StudentFee/${encodeURIComponent(studentId)}/fee-dues`);
  }

  // ---- payments (the school and the collecting user come from the token)
  /** Records a payment against the student's installments and returns its receipt. All or nothing on the server. */
  recordPayment(studentId: string, request: RecordPaymentRequest): Observable<FeeReceipt> {
    return this.api.post<FeeReceipt>(`/api/FeePayment/students/${encodeURIComponent(studentId)}`, request);
  }

  getPayments(studentId: string): Observable<PaymentSummary[]> {
    return this.api.get<PaymentSummary[]>(`/api/FeePayment/students/${encodeURIComponent(studentId)}`).pipe(map((l) => l ?? []));
  }

  /** Soft void: the payment stays (marked Voided) and its amounts come off the installments. */
  voidPayment(paymentId: number, voidReason: string): Observable<PaymentSummary> {
    return this.api.post<PaymentSummary>(`/api/FeePayment/${paymentId}/void`, { voidReason });
  }

  getReceipt(paymentId: number): Observable<FeeReceipt> {
    return this.api.get<FeeReceipt>(`/api/FeePayment/${paymentId}/receipt`);
  }

  // ---- fees (the server takes the school from the token; no school id in these routes)
  getFees(studentId: string): Observable<StudentFees> {
    return this.api.get<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}`);
  }

  assignFeeStructure(studentId: string, feeStructureId: number): Observable<StudentFees> {
    return this.api.put<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}/structure`, { feeStructureId });
  }

  applyConcession(studentId: string, concessionId: number, remarks: string | null): Observable<StudentFees> {
    return this.api.post<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}/concessions`, { concessionId, remarks });
  }

  removeConcession(studentId: string, concessionId: number): Observable<StudentFees> {
    return this.api.delete<StudentFees>(`/api/StudentFee/${encodeURIComponent(studentId)}/concessions/${concessionId}`);
  }

  private withSchool<T>(call: (schoolId: string) => Observable<T>): Observable<T> {
    const schoolId = this.auth.user()?.schoolId;
    return schoolId ? call(encodeURIComponent(schoolId)) : throwError(() => new Error('Your account is not linked to a school.'));
  }
}
