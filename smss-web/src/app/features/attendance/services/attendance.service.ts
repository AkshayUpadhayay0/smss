import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { MySections, SectionAttendance, SubmitAttendanceRequest, SubmitAttendanceResult } from '../models/attendance.model';

const BASE = '/api/Attendance';

/** The school and the acting user come from the token: nothing about them is ever sent. */
@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly api = inject(ApiService);

  /** The sections the caller can mark today (a teacher: their own; a School Admin: every active one). */
  mySections(): Observable<MySections> {
    return this.api.get<MySections>(`${BASE}/my-sections`);
  }

  /** The roster of a section with each student's mark for a day (today when no date). */
  section(sectionId: number, date?: string): Observable<SectionAttendance> {
    return this.api.get<SectionAttendance>(`${BASE}/section/${sectionId}`, date ? { date } : undefined);
  }

  /** The whole section for today in one request; resubmitting the same day overwrites. */
  submit(request: SubmitAttendanceRequest): Observable<SubmitAttendanceResult> {
    return this.api.post<SubmitAttendanceResult>(`${BASE}/submit`, request);
  }
}
