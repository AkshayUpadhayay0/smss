import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ClassRequest, SchoolClass } from '../models/class.model';

const BASE = '/api/Classes';

/** Always "my school's" classes: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class ClassService {
  private readonly api = inject(ApiService);

  list(): Observable<SchoolClass[]> {
    return this.api.get<SchoolClass[]>(BASE).pipe(map((classes) => classes ?? []));
  }

  create(request: ClassRequest): Observable<SchoolClass> {
    return this.api.post<SchoolClass>(BASE, request);
  }

  update(id: number, request: ClassRequest): Observable<SchoolClass> {
    return this.api.put<SchoolClass>(`${BASE}/${id}`, request);
  }

  toggleStatus(id: number): Observable<SchoolClass> {
    return this.api.post<SchoolClass>(`${BASE}/${id}/toggle-status`);
  }
}
