import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { StudentDashboard } from '../models/student-dashboard.model';

/** Student role only: the server resolves the student and school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class StudentDashboardService {
  private readonly api = inject(ApiService);

  get(): Observable<StudentDashboard> {
    return this.api.get<StudentDashboard>('/api/StudentDashboard');
  }
}
