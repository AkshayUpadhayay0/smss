import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ApiService } from '../../../core/services/api.service';
import { CreateSchoolRequest, SchoolRegistrationResponse, SchoolsListModel, UpdateMySchoolProfileRequest, UpdateSchoolRequest } from '../models/school.model';

const BASE = '/api/SchoolRegistration';

@Injectable({ providedIn: 'root' })
export class SchoolService {
  private readonly api = inject(ApiService);

  getSchools(): Observable<SchoolsListModel[]> {
    return this.api.get<SchoolsListModel[]>(`${BASE}/schools`);
  }

  getSchool(schoolId: string): Observable<SchoolsListModel> {
    return this.api.get<SchoolsListModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}`);
  }

  register(request: CreateSchoolRequest): Observable<SchoolRegistrationResponse> {
    return this.api.post<SchoolRegistrationResponse>(`${BASE}/register`, request);
  }

  update(schoolId: string, request: UpdateSchoolRequest): Observable<SchoolsListModel> {
    return this.api.put<SchoolsListModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}`, request);
  }

  // ── "My school" (School Admin): the server resolves the school from the token, so no id is ever sent ──
  getMySchool(): Observable<SchoolsListModel> {
    return this.api.get<SchoolsListModel>(`${BASE}/me`);
  }

  updateMySchool(request: UpdateMySchoolProfileRequest): Observable<SchoolsListModel> {
    return this.api.put<SchoolsListModel>(`${BASE}/me`, request);
  }

  uploadMyLogo(file: File): Observable<SchoolsListModel> {
    return this.api.upload<SchoolsListModel>(`${BASE}/me/logo`, file, 'file', { skipErrorToast: true });
  }

  removeMyLogo(): Observable<SchoolsListModel> {
    return this.api.post<SchoolsListModel>(`${BASE}/me/logo/remove`, {}, { skipErrorToast: true });
  }

  /** Flips Active/Inactive. This is the only way a school's status changes ("delete" is never real). */
  toggleStatus(schoolId: string): Observable<SchoolsListModel> {
    return this.api.post<SchoolsListModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}/toggle-status`);
  }

  /** Callers handle failure themselves (the school itself is already saved), so no global toast. */
  uploadLogo(schoolId: string, file: File): Observable<SchoolsListModel> {
    return this.api.upload<SchoolsListModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}/logo`, file, 'file', { skipErrorToast: true });
  }

  removeLogo(schoolId: string): Observable<SchoolsListModel> {
    return this.api.post<SchoolsListModel>(`${BASE}/schools/${encodeURIComponent(schoolId)}/logo/remove`, {}, { skipErrorToast: true });
  }

  /** API returns a relative path (/uploads/…); make it absolute for <img src>. */
  toLogoUrl(relativeUrl?: string | null): string | null {
    if (!relativeUrl) return null;
    return /^https?:\/\//i.test(relativeUrl) ? relativeUrl : `${environment.apiUrl}${relativeUrl}`;
  }
}
