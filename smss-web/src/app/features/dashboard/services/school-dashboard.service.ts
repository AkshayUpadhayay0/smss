import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SchoolDashboard } from '../models/school-dashboard.model';

/** School Admin only: the server resolves the school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class SchoolDashboardService {
  private readonly api = inject(ApiService);

  summary(): Observable<SchoolDashboard> {
    return this.api.get<SchoolDashboard>('/api/SchoolDashboard/summary');
  }
}
