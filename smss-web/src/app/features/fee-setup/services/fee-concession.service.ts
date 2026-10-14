import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { FeeConcession, FeeConcessionRequest } from '../models/fee-concession.model';

const BASE = '/api/FeeConcession';

/** Always "my school's" concessions: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class FeeConcessionService {
  private readonly api = inject(ApiService);

  list(): Observable<FeeConcession[]> {
    return this.api.get<FeeConcession[]>(BASE).pipe(map((l) => l ?? []));
  }

  create(request: FeeConcessionRequest): Observable<FeeConcession> {
    return this.api.post<FeeConcession>(BASE, request);
  }

  update(id: number, request: FeeConcessionRequest): Observable<FeeConcession> {
    return this.api.put<FeeConcession>(`${BASE}/${id}`, request);
  }

  toggleStatus(id: number): Observable<FeeConcession> {
    return this.api.post<FeeConcession>(`${BASE}/${id}/toggle-status`);
  }
}
