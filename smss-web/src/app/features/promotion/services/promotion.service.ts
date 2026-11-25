import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PromotionPreview, PromotionPreviewRequest } from '../models/promotion.model';

/** School Admin and Principal only. The school comes from the token; no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class PromotionService {
  private readonly api = inject(ApiService);

  /** What promoting a class (or section) WOULD do. Nothing is saved. */
  preview(request: PromotionPreviewRequest): Observable<PromotionPreview> {
    return this.api.post<PromotionPreview>('/api/Promotion/preview', request);
  }
}
