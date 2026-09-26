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


}