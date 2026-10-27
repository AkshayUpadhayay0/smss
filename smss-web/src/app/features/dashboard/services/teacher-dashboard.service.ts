import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { TeacherDashboard } from '../models/teacher-dashboard.model';

/** Teacher role only: the server resolves the employee and school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class TeacherDashboardService {
  private readonly api = inject(ApiService);

  get(): Observable<TeacherDashboard> {
    return this.api.get<TeacherDashboard>('/api/TeacherDashboard');
  }
}
