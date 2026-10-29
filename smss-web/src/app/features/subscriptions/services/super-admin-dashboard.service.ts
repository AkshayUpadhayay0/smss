import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  BillingFrequency,
  BillingTerms,
  InvoiceStatus,
  Paged,
  RecordPaymentRequest,
  SchoolSubscriptionRow,
  SubscriptionInvoice,
  SubscriptionPayment,
  SubscriptionStatus,
  SuperAdminSummary,
} from '../models/super-admin-dashboard.model';

/** Super Admin only: the dashboard numbers and the subscription billing actions. The API refuses every other role. */
@Injectable({ providedIn: 'root' })
export class SuperAdminDashboardService {
  private readonly api = inject(ApiService);

  /** Omit both dates for the server's default: the current month to date. */
  summary(from = '', to = ''): Observable<SuperAdminSummary> {
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return this.api.get<SuperAdminSummary>('/api/SuperAdminDashboard/summary', params);
  }

  schools(search: string, status: SubscriptionStatus | '', page: number, pageSize: number): Observable<Paged<SchoolSubscriptionRow>> {
    const params: Record<string, string | number> = { page, pageSize };
    if (search) params['search'] = search;
    if (status) params['status'] = status;
    return this.api.get<Paged<SchoolSubscriptionRow>>('/api/SuperAdminDashboard/schools', params);
  }

  invoices(schoolId: string, status: InvoiceStatus | '', page: number, pageSize: number): Observable<Paged<SubscriptionInvoice>> {
    const params: Record<string, string | number> = { page, pageSize };
    if (schoolId) params['schoolId'] = schoolId;
    if (status) params['status'] = status;
    return this.api.get<Paged<SubscriptionInvoice>>('/api/SubscriptionInvoice', params);
  }

  invoice(invoiceId: number): Observable<SubscriptionInvoice> {
    return this.api.get<SubscriptionInvoice>(`/api/SubscriptionInvoice/${invoiceId}`);
  }

  configureBilling(schoolId: string, billingFrequency: BillingFrequency, terms: BillingTerms): Observable<unknown> {
    return this.api.post(`/api/SchoolSubscription/${encodeURIComponent(schoolId)}/configure`, { billingFrequency, ...terms });
  }

  /** Replaces the school's negotiated terms (null = global). Applies to invoices generated from now on. */
  updateTerms(schoolId: string, terms: BillingTerms): Observable<unknown> {
    return this.api.put(`/api/SchoolSubscription/${encodeURIComponent(schoolId)}/terms`, terms);
  }

  generateInvoice(schoolId: string): Observable<SubscriptionInvoice> {
    return this.api.post<SubscriptionInvoice>(`/api/SubscriptionInvoice/${encodeURIComponent(schoolId)}/generate`, {});
  }

  recordPayment(request: RecordPaymentRequest): Observable<SubscriptionPayment> {
    return this.api.post<SubscriptionPayment>('/api/SubscriptionPayment', request);
  }

  voidPayment(paymentId: number, voidReason: string): Observable<SubscriptionPayment> {
    return this.api.post<SubscriptionPayment>(`/api/SubscriptionPayment/${paymentId}/void`, { voidReason });
  }
}
