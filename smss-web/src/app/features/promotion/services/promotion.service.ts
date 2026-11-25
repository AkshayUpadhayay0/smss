import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PromotionCorrectionCheck, PromotionCorrectionResult, PromotionExecuteRequest, PromotionPending, PromotionPreview, PromotionPreviewRequest, PromotionResult } from '../models/promotion.model';

/** School Admin and Principal only. The school comes from the token; no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class PromotionService {
  private readonly api = inject(ApiService);

  /** What promoting a class (or section) WOULD do. Nothing is saved. */
  preview(request: PromotionPreviewRequest): Observable<PromotionPreview> {
    return this.api.post<PromotionPreview>('/api/Promotion/preview', request);
  }

  /** Runs the promotion (saves changes). The server recomputes everything; only the admin's choices are used. */
  execute(request: PromotionExecuteRequest): Observable<PromotionResult> {
    return this.api.post<PromotionResult>('/api/Promotion/execute', request);
  }

  /** The students waiting to be promoted into the current year. summary = the count only (the dashboard tile). */
  pending(summary = false): Observable<PromotionPending> {
    // the tile shows "Unavailable" itself, so a failure must not also raise the global toast
    return this.api.get<PromotionPending>('/api/Promotion/pending', summary ? { summary: true } : undefined, { skipErrorToast: summary });
  }

  /** What correcting ONE student's promotion (out of the given year) would remove, or why it is refused. Nothing is changed. */
  checkCorrection(studentId: string, sourceYearId: number): Observable<PromotionCorrectionCheck> {
    return this.api.get<PromotionCorrectionCheck>('/api/Promotion/corrections/check', { studentId, sourceYearId });
  }

  /** Removes the new-year enrolment and its unpaid fees, clears the outcome, writes the audit row. */
  correct(studentId: string, sourceYearId: number, reason: string): Observable<PromotionCorrectionResult> {
    return this.api.post<PromotionCorrectionResult>('/api/Promotion/corrections', { studentId, sourceYearId, reason, confirm: true });
  }
}
