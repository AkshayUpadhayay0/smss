import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApiResponse, MasterItem } from '../models/master-data.model';

@Injectable({ providedIn: 'root' })
export class MasterAdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/MasterData`;

  getAll(path: string, includeInactive = true): Observable<ApiResponse<MasterItem[]>> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ApiResponse<MasterItem[]>>(`${this.baseUrl}/${path}`, { params });
  }

  create(path: string, body: Record<string, unknown>): Observable<ApiResponse<MasterItem>> {
    return this.http.post<ApiResponse<MasterItem>>(`${this.baseUrl}/${path}`, body);
  }

  update(path: string, id: number, body: Record<string, unknown>): Observable<ApiResponse<MasterItem>> {
    return this.http.put<ApiResponse<MasterItem>>(`${this.baseUrl}/${path}/${id}`, body);
  }

  toggleStatus(path: string, id: number): Observable<ApiResponse<MasterItem>> {
    return this.http.post<ApiResponse<MasterItem>>(`${this.baseUrl}/${path}/${id}/toggle-status`, {});
  }
}

/** Converts any HTTP failure into a safe, user-readable message. */
export function extractApiError(err: HttpErrorResponse): string {
  if (err.status === 0) return 'Unable to reach the server. Check your connection.';
  if (err.status === 401) return 'Your session has expired. Please log in again.';
  if (err.status === 403) return 'You do not have permission to perform this action.';

  const body = err.error;
  if (body?.message) return body.message;

  // ASP.NET default model-validation shape: { errors: { Field: ["msg"] } }
  if (body?.errors) {
    const first = Object.values(body.errors as Record<string, string[]>)[0];
    if (first?.length) return first[0];
  }
  return 'Something went wrong. Please try again.';
}