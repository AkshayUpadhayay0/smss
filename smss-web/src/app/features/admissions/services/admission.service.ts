import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { Admission, AdmissionDetail, ConfirmAdmissionRequest, SubmitAdmissionRequest } from '../models/admission.model';

const BASE = '/api/Admission';

/**
 * The routes carry the school id. It always comes from the signed-in session, and the server refuses any id that
 * isn't the caller's own school. The acting user (reviewer / approver) is taken from the token server-side, so no
 * user id is sent from here.
 */
@Injectable({ providedIn: 'root' })
export class AdmissionService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  getAll(): Observable<Admission[]> {
    return this.withSchool((s) => this.api.get<Admission[]>(`${BASE}/${s}`));
  }

  getById(admissionId: number): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.get<AdmissionDetail>(`${BASE}/${s}/${admissionId}`));
  }

  submit(payload: SubmitAdmissionRequest): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/submit`, payload));
  }

  startReview(admissionId: number): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/start-review`));
  }

  approve(admissionId: number, remarks: string | null): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/approve`, { remarks }));
  }

  reject(admissionId: number, remarks: string): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/reject`, { remarks }));
  }

  cancel(admissionId: number, remarks: string | null): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/cancel`, { remarks }));
  }

  confirmAdmission(admissionId: number, payload: ConfirmAdmissionRequest): Observable<AdmissionDetail> {
    return this.withSchool((s) => this.api.post<AdmissionDetail>(`${BASE}/${s}/${admissionId}/confirm`, payload));
  }

  private withSchool<T>(call: (schoolId: string) => Observable<T>): Observable<T> {
    const schoolId = this.auth.user()?.schoolId;
    return schoolId ? call(encodeURIComponent(schoolId)) : throwError(() => new Error('Your account is not linked to a school.'));
  }
}
