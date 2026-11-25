import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { FeeRepairPreview, FeeRepairResult } from '../models/fee-repair.model';

const BASE = '/api/FeeRepair';

/** School Admin only. The school comes from the token; no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class FeeRepairService {
  private readonly api = inject(ApiService);

  /** Who is missing fees in the year, and which classes have no fee structure. Nothing is saved. */
  missing(academicYearId: number): Observable<FeeRepairPreview> {
    return this.api.get<FeeRepairPreview>(`${BASE}/years/${academicYearId}/missing`);
  }

  assign(academicYearId: number): Observable<FeeRepairResult> {
    return this.api.post<FeeRepairResult>(`${BASE}/years/${academicYearId}/assign`, {});
  }
}
