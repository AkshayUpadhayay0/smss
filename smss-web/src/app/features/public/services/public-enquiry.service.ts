import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse } from '../../../core/models/api-response.model';
import { ApiService } from '../../../core/services/api.service';
import { CreateEnquiryRequest } from '../models/enquiry.model';

/** What went wrong with a submission, in a form the page can show. */
export interface EnquiryFailure {
  /** Friendly message, never technical detail. */
  message: string;
  /** Field name (as in the request) -> message, from a 400 answer. */
  fields: Record<string, string>;
  rateLimited: boolean;
}

export const GENERIC_ENQUIRY_ERROR = 'Sorry, we could not send your request right now. Please try again in a little while.';

@Injectable({ providedIn: 'root' })
export class PublicEnquiryService {
  private readonly api = inject(ApiService);

  /** Anonymous call. The page shows its own error UI, so the global error toast is skipped. */
  submit(request: CreateEnquiryRequest): Observable<void> {
    return this.api.post<unknown>('/api/public/enquiries', request, { skipErrorToast: true }).pipe(map(() => undefined));
  }

  /** Turns any failure into something safe to show. */
  toFailure(error: unknown): EnquiryFailure {
    const failure: EnquiryFailure = { message: GENERIC_ENQUIRY_ERROR, fields: {}, rateLimited: false };
    if (!(error instanceof HttpErrorResponse)) return failure;
    const body = error.error as Partial<ApiResponse<Record<string, string>>> | null;
    if (error.status === 429) {
      return { ...failure, rateLimited: true, message: 'You have sent several requests in a short time. Please wait a few minutes and try again.' };
    }
    if (error.status === 400 && body && typeof body === 'object' && body.data && typeof body.data === 'object') {
      const fields = Object.fromEntries(Object.entries(body.data).filter(([, v]) => typeof v === 'string')) as Record<string, string>;
      return { ...failure, fields, message: 'Please correct the highlighted fields and try again.' };
    }
    return failure;
  }
}
