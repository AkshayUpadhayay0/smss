import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SchoolAdminDashboard } from '../models/school-admin-dashboard.model';

/** School Admin only: the server resolves the school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class SchoolAdminDashboardService {
  private readonly api = inject(ApiService);

  /** academicYearId omitted = the current year. It only chooses what to LOOK at; it never changes the school's current year. */
  load(academicYearId?: number): Observable<SchoolAdminDashboard> {
    return this.api.get<SchoolAdminDashboard>('/api/Dashboard/school-admin', academicYearId ? { academicYearId } : undefined);
  }
}
