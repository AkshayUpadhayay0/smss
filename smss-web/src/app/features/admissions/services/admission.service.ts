import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  Admission,
  AdmissionDetail,
  AdmissionDocument,
  AdmissionFilters,
  ConfirmAdmissionRequest,
  SubmitAdmissionRequest,
} from '../models/admission.model';

const BASE = '/api/Admission';

/**
 * The routes carry the school id. It always comes from the signed-in session, and the server refuses any id that
 * is not the caller's own school. The acting user (reviewer / approver) is taken from the token server-side, so no
 * user id is sent from here.
 */
@Injectable({ providedIn: 'root' })
export class AdmissionService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  getAll(filters: AdmissionFilters = {}): Observable<Admission[]> {
    const params: Record<string, string | number> = {};
    if (filters.status) params['status'] = filters.status;
    if (filters.classId) params['classId'] = filters.classId;
    if (filters.academicYearId) params['academicYearId'] = filters.academicYearId;
    return this.withSchool((s) => this.api.get<Admission[]>(`${BASE}/${s}`, params));
  }

  getById(admissionId: number): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.get<AdmissionDetail>(`${BASE}/${s}/${admissionId}`));
  }

  submit(payload: SubmitAdmissionRequest): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/submit`, payload));
  }

  /** Only while Submitted or Under Review. */
  update(admissionId: number, payload: SubmitAdmissionRequest): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.put<AdmissionDetail>(`${BASE}/${s}/${admissionId}`, payload));
  }

  startReview(admissionId: number, notes: string | null = null): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/start-review`, { notes }));
  }

  approve(admissionId: number, remarks: string | null): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/approve`, { remarks }));
  }

  reject(admissionId: number, remarks: string): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/reject`, { remarks }));
  }

  /** Withdraw: the applicant pulled out (server status CANCELLED). Refused once a decision has been made. */
  cancel(admissionId: number, remarks: string | null): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/cancel`, { remarks }));
  }

  confirmAdmission(admissionId: number, payload: ConfirmAdmissionRequest): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/confirm`, payload));
  }

  // ---- documents (private files: fetched only through the authenticated download)
  documents(admissionId: number): Observable<AdmissionDocument[]> {
    return this.withSchool((s) => this.api.get<AdmissionDocument[]>(`${BASE}/${s}/${admissionId}/documents`));
  }

  uploadDocument(admissionId: number, file: File, documentTypeId: number, remarks: string | null): Observable<AdmissionDocument> {
    const extra: Record<string, string> = { documentTypeId: String(documentTypeId) };
    if (remarks) extra['remarks'] = remarks;
    return this.withSchool((s) => this.api.upload<AdmissionDocument>(`${BASE}/${s}/${admissionId}/documents`, file, 'file', undefined, extra));
  }

  downloadDocument(admissionId: number, documentId: number): Observable<Blob> {
    return this.withSchool((s) => this.api.download(`${BASE}/${s}/${admissionId}/documents/${documentId}/download`));
  }

  removeDocument(admissionId: number, documentId: number): Observable<AdmissionDocument> {
    return this.withSchool((s) => this.api.delete<AdmissionDocument>(`${BASE}/${s}/${admissionId}/documents/${documentId}`));
  }

  private withSchool<T>(call: (schoolId: string) => Observable<T>): Observable<T> {
    const schoolId = this.auth.user()?.schoolId;
    return schoolId ? call(encodeURIComponent(schoolId)) : throwError(() => new Error('Your account is not linked to a school.'));
  }
}
