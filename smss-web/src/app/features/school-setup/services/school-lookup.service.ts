import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SchoolLookupItem } from '../models/school-lookup.model';

/** Generic CRUD against /api/{path} for the school-owned lookups; the server scopes everything to the token's school. */
@Injectable({ providedIn: 'root' })
export class SchoolLookupService {
  private readonly api = inject(ApiService);

  list(path: string): Observable<SchoolLookupItem[]> {
    return this.api.get<SchoolLookupItem[]>(`/api/${path}`).pipe(map((items) => items ?? []));
  }

  create(path: string, body: Record<string, unknown>): Observable<SchoolLookupItem> {
    return this.api.post<SchoolLookupItem>(`/api/${path}`, body);
  }

  update(path: string, id: number, body: Record<string, unknown>): Observable<SchoolLookupItem> {
    return this.api.put<SchoolLookupItem>(`/api/${path}/${id}`, body);
  }

  toggleStatus(path: string, id: number): Observable<SchoolLookupItem> {
    return this.api.post<SchoolLookupItem>(`/api/${path}/${id}/toggle-status`);
  }
}
