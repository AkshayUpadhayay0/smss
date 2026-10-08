import { Injectable, inject } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { MasterDataService } from '../../../core/services/master-data.service';
import { MasterItem } from '../models/master.model';

const BASE = '/api/MasterData';

/** Generic CRUD against /api/MasterData/{path}; the same four calls serve all five masters. */
@Injectable({ providedIn: 'root' })
export class MasterCrudService {
  private readonly api = inject(ApiService);
  private readonly lookups = inject(MasterDataService);

  /** `includeInactive` is required: the API hides inactive records by default, which would make Activate unreachable. */
  list(path: string): Observable<MasterItem[]> {
    return this.api.get<MasterItem[]>(`${BASE}/${path}`, { includeInactive: true }).pipe(map((items) => items ?? []));
  }

  create(path: string, body: Record<string, unknown>): Observable<MasterItem> {
    return this.api.post<MasterItem>(`${BASE}/${path}`, body).pipe(tap(() => this.lookups.invalidate()));
  }

  update(path: string, id: number | string, body: Record<string, unknown>): Observable<MasterItem> {
    return this.api.put<MasterItem>(`${BASE}/${path}/${id}`, body).pipe(tap(() => this.lookups.invalidate()));
  }

  /** Flips active/inactive. Nothing is ever really deleted. */
  toggleStatus(path: string, id: number | string): Observable<MasterItem> {
    return this.api.post<MasterItem>(`${BASE}/${path}/${id}/toggle-status`).pipe(tap(() => this.lookups.invalidate()));
  }
}
