import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { EnquiryDetail, EnquiryListResult, EnquiryQuery, EnquirySummary, EnquiryStatus } from '../models/enquiry-admin.model';

const BASE = '/api/superadmin/enquiries';

/** Super Admin's view of the public enquiries. Also holds the NEW count the sidebar badge shows. */
@Injectable({ providedIn: 'root' })
export class EnquiryService {
  private readonly api = inject(ApiService);

  private readonly _summary = signal<EnquirySummary | null>(null);
  /** Latest summary seen (null until loaded, or if it could not be). */
  readonly summary = this._summary.asReadonly();

  list(query: EnquiryQuery): Observable<EnquiryListResult> {
    const params: Record<string, string | number> = { page: query.page, pageSize: query.pageSize };
    if (query.type) params['type'] = query.type;
    if (query.status) params['status'] = query.status;
    if (query.from) params['from'] = query.from;
    if (query.to) params['to'] = query.to;
    if (query.search?.trim()) params['search'] = query.search.trim();
    return this.api.get<EnquiryListResult>(BASE, params);
  }

  get(id: number): Observable<EnquiryDetail> {
    return this.api.get<EnquiryDetail>(`${BASE}/${id}`);
  }

  /** POST, not PATCH: PATCH is blocked in this project's dev environment. The handling user is taken from the token by the API. */
  updateStatus(id: number, status: EnquiryStatus, adminNotes: string | null): Observable<EnquiryDetail> {
    return this.api.post<EnquiryDetail>(`${BASE}/${id}/status`, { status, adminNotes }).pipe(tap(() => this.refreshSummary()));
  }

  fetchSummary(): Observable<EnquirySummary> {
    return this.api.get<EnquirySummary>(`${BASE}/summary`, undefined, { skipErrorToast: true }).pipe(tap((s) => this._summary.set(s)));
  }

  /** Fire and forget (the sidebar badge and dashboard widgets). A failure leaves the previous numbers. */
  refreshSummary(): void {
    this.fetchSummary().subscribe({ error: () => undefined });
  }
}
