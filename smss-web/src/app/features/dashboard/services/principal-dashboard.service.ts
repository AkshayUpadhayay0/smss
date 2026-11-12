import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PrincipalDashboard } from '../models/principal-dashboard.model';

/** Principal only: the server resolves the school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class PrincipalDashboardService {
  private readonly api = inject(ApiService);

  summary(): Observable<PrincipalDashboard> {
    return this.api.get<PrincipalDashboard>('/api/PrincipalDashboard/summary');
  }
}
