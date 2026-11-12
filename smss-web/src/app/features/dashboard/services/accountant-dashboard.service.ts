import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AccountantDashboard } from '../models/accountant-dashboard.model';

/** Accountant only: the server resolves the school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class AccountantDashboardService {
  private readonly api = inject(ApiService);

  summary(): Observable<AccountantDashboard> {
    return this.api.get<AccountantDashboard>('/api/AccountantDashboard/summary');
  }
}
