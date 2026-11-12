import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/services/api.service';

/** What a reset returns. The password is shown once and must never be kept, logged or cached anywhere. */
export interface PasswordResetResult {
  loginId: string;
  displayName: string;
  temporaryPassword: string;
}

export const RESET_REASON_MAX = 500;

/**
 * Admin-initiated password reset. The server decides who may reset whom (Super Admin: a School Admin; School Admin: any login of their own school)
 * and always takes the school from the token, so only the target's login id and a reason are sent.
 */
@Injectable({ providedIn: 'root' })
export class PasswordResetService {
  private readonly api = inject(ApiService);

  reset(loginId: string, reason: string): Observable<PasswordResetResult> {
    return this.api.post<PasswordResetResult>('/api/PasswordReset', { loginId, reason: reason.trim() });
  }
}
