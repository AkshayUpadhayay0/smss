import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { TaxType, TaxTypeRequest } from '../models/tax-type.model';

const BASE = '/api/TaxType';

/** Always "my school's" tax types: the server resolves the school from the token, so no school id is ever sent. No hard delete exists: a type is deactivated. */
@Injectable({ providedIn: 'root' })
export class TaxTypeService {
  private readonly api = inject(ApiService);

  /** Active types only by default; `includeInactive` is honoured by the server for a School Admin only. */
  list(includeInactive = false): Observable<TaxType[]> {
    return this.api.get<TaxType[]>(BASE, includeInactive ? { includeInactive: true } : undefined).pipe(map((l) => l ?? []));
  }

  create(request: TaxTypeRequest): Observable<TaxType> {
    return this.api.post<TaxType>(BASE, request);
  }

  update(id: number, request: TaxTypeRequest): Observable<TaxType> {
    return this.api.put<TaxType>(`${BASE}/${id}`, request);
  }

  activate(id: number): Observable<TaxType> {
    return this.api.post<TaxType>(`${BASE}/${id}/activate`);
  }

  deactivate(id: number): Observable<TaxType> {
    return this.api.post<TaxType>(`${BASE}/${id}/deactivate`);
  }
}
