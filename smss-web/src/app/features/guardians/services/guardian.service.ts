import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { BulkWhatsAppResult, Guardian, GuardianUpdateRequest, IncompleteGuardians, SiblingStudent } from '../models/guardian.model';

const BASE = '/api/Guardians';

/** Parents' contact details. The school always comes from the signed-in session on the server; nothing here names it. */
@Injectable({ providedIn: 'root' })
export class GuardianService {
  private readonly api = inject(ApiService);

  get(guardianId: number): Observable<Guardian> {
    return this.api.get<Guardian>(`${BASE}/${guardianId}`);
  }

  update(guardianId: number, request: GuardianUpdateRequest): Observable<Guardian> {
    return this.api.put<Guardian>(`${BASE}/${guardianId}`, request);
  }

  /** Primary guardians of active students lacking a WhatsApp number or an email (class and section from the current-flagged year). */
  incomplete(filters: { classId?: number; sectionId?: number; search?: string; page?: number; pageSize?: number } = {}): Observable<IncompleteGuardians> {
    const params: Record<string, string | number> = { page: filters.page ?? 1, pageSize: filters.pageSize ?? 25 };
    if (filters.classId) params['classId'] = filters.classId;
    if (filters.sectionId) params['sectionId'] = filters.sectionId;
    if (filters.search?.trim()) params['search'] = filters.search.trim();
    return this.api.get<IncompleteGuardians>(`${BASE}/incomplete`, params);
  }

  /** The sibling pick: active students of this school with their parents (mobile numbers masked). Needs a class or at least 2 letters of a name. */
  siblings(filters: { classId?: number; search?: string; limit?: number } = {}): Observable<SiblingStudent[]> {
    const params: Record<string, string | number> = { limit: filters.limit ?? 20 };
    if (filters.classId) params['classId'] = filters.classId;
    if (filters.search?.trim()) params['search'] = filters.search.trim();
    return this.api.get<SiblingStudent[]>(`${BASE}/siblings`, params);
  }

  incompleteCount(): Observable<number> {
    return this.api.get<number>(`${BASE}/incomplete/count`);
  }

  /** For the ticked guardians only: use the mobile number as the WhatsApp number. All or nothing; email is never touched. */
  useMobileAsWhatsApp(guardianIds: number[]): Observable<BulkWhatsAppResult> {
    return this.api.post<BulkWhatsAppResult>(`${BASE}/bulk-whatsapp`, { guardianIds });
  }
}
