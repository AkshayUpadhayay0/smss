import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { LateFeePreview, LateFeePreviewRequest, LateFeeRule, SaveLateFeeRuleRequest } from '../models/late-fee.model';

const BASE = '/api/LateFee';

/** The school comes from the token: no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class LateFeeService {
  private readonly api = inject(ApiService);

  /** The rule and the fee type flags. School Admin, Accountant and Principal may read it. */
  getRule(): Observable<LateFeeRule> {
    return this.api.get<LateFeeRule>(`${BASE}/rule`, undefined, { skipErrorToast: true });
  }

  /** School Admin only. A new rule applies from the next payment. */
  saveRule(request: SaveLateFeeRuleRequest): Observable<LateFeeRule> {
    return this.api.put<LateFeeRule>(`${BASE}/rule`, request);
  }

  /** What late fee a payment like this WOULD be charged (read-only). The payment screen shows it; the client never sends an amount. */
  preview(studentId: string, request: LateFeePreviewRequest): Observable<LateFeePreview> {
    return this.api.post<LateFeePreview>(`${BASE}/preview/${encodeURIComponent(studentId)}`, request, { skipErrorToast: true });
  }
}
