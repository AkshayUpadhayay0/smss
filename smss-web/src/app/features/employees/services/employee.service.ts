import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  Employee,
  EmployeeDocument,
  EmployeeFilters,
  EmployeeLoginCreated,
  EmployeeLookups,
  EmployeePage,
  EmployeePhoto,
  EmployeeRequest,
} from '../models/employee.model';

const BASE = '/api/Employee';

/** Always "my school's" employees: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private readonly api = inject(ApiService);

  lookups(): Observable<EmployeeLookups> {
    return this.api.get<EmployeeLookups>(`${BASE}/lookups`);
  }

  /** Filtering and paging happen on the server. */
  list(filters: EmployeeFilters, page: number, pageSize: number): Observable<EmployeePage> {
    const params: Record<string, string | number> = { page, pageSize };
    for (const [key, value] of Object.entries(filters)) if (value && value !== 'All') params[key] = value;
    return this.api.get<EmployeePage>(BASE, params);
  }

  get(employeeId: string): Observable<Employee> {
    return this.api.get<Employee>(`${BASE}/${encodeURIComponent(employeeId)}`);
  }

  create(request: EmployeeRequest): Observable<Employee> {
    return this.api.post<Employee>(BASE, request);
  }

  update(employeeId: string, request: EmployeeRequest): Observable<Employee> {
    return this.api.put<Employee>(`${BASE}/${encodeURIComponent(employeeId)}`, request);
  }

  toggleStatus(employeeId: string): Observable<Employee> {
    return this.api.post<Employee>(`${BASE}/${encodeURIComponent(employeeId)}/toggle-status`);
  }

  uploadPhoto(employeeId: string, file: File): Observable<EmployeePhoto> {
    return this.api.upload<EmployeePhoto>(`${BASE}/${encodeURIComponent(employeeId)}/photo`, file);
  }

  removePhoto(employeeId: string): Observable<EmployeePhoto> {
    return this.api.post<EmployeePhoto>(`${BASE}/${encodeURIComponent(employeeId)}/photo/remove`);
  }

  documents(employeeId: string): Observable<EmployeeDocument[]> {
    return this.api.get<EmployeeDocument[]>(`${BASE}/${encodeURIComponent(employeeId)}/documents`).pipe(map((l) => l ?? []));
  }

  uploadDocument(employeeId: string, file: File, documentTypeId: number, remarks: string | null): Observable<EmployeeDocument> {
    const fields: Record<string, string> = { documentTypeId: String(documentTypeId) };
    if (remarks) fields['remarks'] = remarks;
    return this.api.upload<EmployeeDocument>(`${BASE}/${encodeURIComponent(employeeId)}/documents`, file, 'file', undefined, fields);
  }

  downloadDocument(employeeId: string, documentId: number): Observable<Blob> {
    return this.api.download(`${BASE}/${encodeURIComponent(employeeId)}/documents/${documentId}/download`);
  }

  removeDocument(employeeId: string, documentId: number): Observable<EmployeeDocument> {
    return this.api.post<EmployeeDocument>(`${BASE}/${encodeURIComponent(employeeId)}/documents/${documentId}/remove`);
  }

  /** Returns the generated credentials ONCE. */
  createLogin(employeeId: string, roleIds: number[]): Observable<EmployeeLoginCreated> {
    return this.api.post<EmployeeLoginCreated>(`${BASE}/${encodeURIComponent(employeeId)}/create-login`, { roleIds });
  }
}
