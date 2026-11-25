import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PromotionExecuteRequest, PromotionPending, PromotionPreview, PromotionPreviewRequest, PromotionResult } from '../models/promotion.model';

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
}
