import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  BackdatingPreview,
  BackdatingResult,
  CreateFeeStructureRequest,
  FeeFrequency,
  FeeStructure,
  FeeStructureSummary,
  FeeTypeOption,
  UpdateFeeStructureRequest,
} from '../models/fee-structure.model';

const BASE = '/api/FeeStructure';

/** Always "my school's" fee structures: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class FeeStructureService {
  private readonly api = inject(ApiService);

  list(filter: { academicYearId?: number; classId?: number } = {}): Observable<FeeStructureSummary[]> {
    const params: Record<string, number> = {};
    if (filter.academicYearId) params['academicYearId'] = filter.academicYearId;
    if (filter.classId) params['classId'] = filter.classId;
    return this.api.get<FeeStructureSummary[]>(BASE, params).pipe(map((l) => l ?? []));
  }

  /** The header AND its lines (fee type + frequency names joined) in one response. */
  get(id: number): Observable<FeeStructure> {
    return this.api.get<FeeStructure>(`${BASE}/${id}`);
  }

  create(request: CreateFeeStructureRequest): Observable<FeeStructure> {
    return this.api.post<FeeStructure>(BASE, request);
  }

  /** `skipErrorToast`: the builder answers the "confirm the removal of unpaid dues" 409 itself. */
  update(id: number, request: UpdateFeeStructureRequest): Observable<FeeStructure> {
    return this.api.put<FeeStructure>(`${BASE}/${id}`, request, { skipErrorToast: true });
  }

  backdatingPreview(id: number, feeTypeId: number, fromDate: string): Observable<BackdatingPreview> {
    return this.api.post<BackdatingPreview>(`${BASE}/${id}/lines/${feeTypeId}/backdating/preview`, { fromDate });
  }

  backdatingApply(id: number, feeTypeId: number, body: { fromDate: string; reason: string; expectedDuesCount: number }): Observable<BackdatingResult> {
    return this.api.post<BackdatingResult>(`${BASE}/${id}/lines/${feeTypeId}/backdating/apply`, { ...body, confirm: true });
  }

  toggleStatus(id: number): Observable<FeeStructure> {
    return this.api.post<FeeStructure>(`${BASE}/${id}/toggle-status`);
  }

  frequencies(): Observable<FeeFrequency[]> {
    return this.api.get<FeeFrequency[]>(`${BASE}/frequencies`).pipe(map((l) => l ?? []));
  }

  /** The school's Fee Types master (the builder's fee head dropdown). */
  feeTypes(): Observable<FeeTypeOption[]> {
    return this.api.get<FeeTypeOption[]>('/api/FeeType').pipe(map((l) => l ?? []));
  }
}
