import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  BulkParentAccountPreview,
  BulkParentAccountResult,
  LinkCheck,
  ParentAccountResult,
  ParentAccountStatus,
  PasswordSet,
} from '../models/parent-account.model';

/**
 * Parent accounts. The PUBLIC calls (check-link, set-password, forgot) send their secret in the request BODY and never in a URL; the page that reads the token from the address bar removes it at once.
 * The staff calls carry no token, no link and no password: the school asks the system to send one.
 */
@Injectable({ providedIn: 'root' })
export class ParentAccountService {
  private readonly api = inject(ApiService);

  // ---- public
  checkLink(token: string): Observable<LinkCheck> {
    return this.api.post<LinkCheck>('/api/parent/auth/check-link', { token }, { skipErrorToast: true });
  }

  /** The server's reason (a refused weak password, a link that no longer works) is shown by the page itself. */
  setPassword(token: string, newPassword: string): Observable<PasswordSet> {
    return this.api.post<PasswordSet>('/api/parent/auth/set-password', { token, newPassword }, { skipErrorToast: true });
  }

  /** ALWAYS the same answer, whether or not the contact matches an account. */
  forgot(contact: string): Observable<unknown> {
    return this.api.post<unknown>('/api/parent/auth/forgot', { contact }, { skipErrorToast: true });
  }

  // ---- staff
  status(guardianId: number): Observable<ParentAccountStatus> {
    return this.api.get<ParentAccountStatus>(`/api/ParentAccounts/${guardianId}`);
  }

  resend(guardianId: number): Observable<ParentAccountResult> {
    return this.api.post<ParentAccountResult>(`/api/ParentAccounts/${guardianId}/resend`, {});
  }

  reset(guardianId: number): Observable<ParentAccountResult> {
    return this.api.post<ParentAccountResult>(`/api/ParentAccounts/${guardianId}/reset`, {});
  }

  bulkPreview(): Observable<BulkParentAccountPreview> {
    return this.api.get<BulkParentAccountPreview>('/api/ParentAccounts/bulk/preview');
  }

  bulkCreate(confirmSchoolHoldsParentConsent: boolean): Observable<BulkParentAccountResult> {
    return this.api.post<BulkParentAccountResult>('/api/ParentAccounts/bulk/create', { confirmSchoolHoldsParentConsent });
  }
}
