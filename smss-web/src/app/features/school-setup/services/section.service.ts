import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { Section, SectionRequest } from '../models/section.model';

const BASE = '/api/Sections';

/** Always "my school's" sections: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class SectionService {
  private readonly api = inject(ApiService);

  list(): Observable<Section[]> {
    return this.api.get<Section[]>(BASE).pipe(map((sections) => sections ?? []));
  }

  create(request: SectionRequest): Observable<Section> {
    return this.api.post<Section>(BASE, request);
  }

  update(id: number, request: SectionRequest): Observable<Section> {
    return this.api.put<Section>(`${BASE}/${id}`, request);
  }

  toggleStatus(id: number): Observable<Section> {
    return this.api.post<Section>(`${BASE}/${id}/toggle-status`);
  }
}
