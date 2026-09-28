import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ApiResponse,
  CreateSchoolRequest,
  SchoolRegistrationResponse,
  SchoolsListModel,
  UpdateSchoolRequest
} from '../model/school.model';

@Injectable({ providedIn: 'root' })
export class SchoolService {
  private readonly baseUrl = `${environment.apiUrl}/api/SchoolRegistration`;

  constructor(private http: HttpClient) { }

  getSchools(): Observable<ApiResponse<SchoolsListModel[]>> {
    return this.http.get<ApiResponse<SchoolsListModel[]>>(`${this.baseUrl}/schools`);
  }

  getSchoolById(schoolId: string): Observable<ApiResponse<SchoolsListModel>> {
    return this.http.get<ApiResponse<SchoolsListModel>>(`${this.baseUrl}/schools/${schoolId}`);
  }

  registerSchool(payload: CreateSchoolRequest): Observable<ApiResponse<SchoolRegistrationResponse>> {
    return this.http.post<ApiResponse<SchoolRegistrationResponse>>(`${this.baseUrl}/register`, payload);
  }

  updateSchool(schoolId: string, payload: UpdateSchoolRequest): Observable<ApiResponse<SchoolsListModel>> {
    return this.http.put<ApiResponse<SchoolsListModel>>(`${this.baseUrl}/schools/${schoolId}`, payload);
  }

  toggleSchoolStatus(schoolId: string): Observable<ApiResponse<SchoolsListModel>> {
    return this.http.post<ApiResponse<SchoolsListModel>>(`${this.baseUrl}/schools/${schoolId}/toggle-status`, {});
  }

  // multipart upload: do NOT set Content-Type manually, the browser adds the boundary
  uploadLogo(schoolId: string, file: File): Observable<ApiResponse<SchoolsListModel>> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    return this.http.post<ApiResponse<SchoolsListModel>>(`${this.baseUrl}/schools/${schoolId}/logo`, formData);
  }

  removeLogo(schoolId: string): Observable<ApiResponse<SchoolsListModel>> {
    return this.http.post<ApiResponse<SchoolsListModel>>(`${this.baseUrl}/schools/${schoolId}/logo/remove`, {});
  }

  // The API returns a relative URL (/uploads/...); make it absolute for <img src>
  toLogoUrl(relativeUrl?: string | null): string | null {
    if (!relativeUrl) return null;
    return /^https?:\/\//i.test(relativeUrl) ? relativeUrl : `${environment.apiUrl}${relativeUrl}`;
  }
}