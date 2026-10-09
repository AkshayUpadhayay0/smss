import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { DocumentType, DocumentTypeRequest } from '../models/document-type.model';

const BASE = '/api/DocumentType';

/**
 * The document-type routes carry the school id. It always comes from the signed-in session (never typed or
 * hard-coded), and the server independently refuses any id that isn't the caller's own school.
 */
@Injectable({ providedIn: 'root' })
export class DocumentTypeService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  list(): Observable<DocumentType[]> {
    return this.withSchool((id) => this.api.get<DocumentType[]>(`${BASE}/${id}`));
  }

  create(request: DocumentTypeRequest): Observable<DocumentType> {
    return this.withSchool((id) => this.api.post<DocumentType>(`${BASE}/${id}`, request));
  }

  update(documentTypeId: number, request: DocumentTypeRequest): Observable<DocumentType> {
    return this.withSchool((id) => this.api.post<DocumentType>(`${BASE}/${id}/update`, { documentTypeId, ...request }));
  }

  toggleStatus(documentTypeId: number): Observable<DocumentType> {
    return this.withSchool((id) => this.api.post<DocumentType>(`${BASE}/${id}/${documentTypeId}/toggle-status`));
  }

  private withSchool<T>(call: (schoolId: string) => Observable<T>): Observable<T> {
    const schoolId = this.auth.user()?.schoolId;
    return schoolId ? call(encodeURIComponent(schoolId)) : throwError(() => new Error('Your account is not linked to a school.'));
  }
}
