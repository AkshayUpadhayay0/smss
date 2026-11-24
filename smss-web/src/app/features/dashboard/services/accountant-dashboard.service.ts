import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AccountantDashboard } from '../models/accountant-dashboard.model';

/** Accountant only: the server resolves the school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class AccountantDashboardService {
  private readonly api = inject(ApiService);

  /** academicYearId omitted = the school's current year. It only chooses which year's DUES are shown. */
  summary(academicYearId?: number): Observable<AccountantDashboard> {
    return this.api.get<AccountantDashboard>('/api/AccountantDashboard/summary', academicYearId ? { academicYearId } : undefined);
  }
}
