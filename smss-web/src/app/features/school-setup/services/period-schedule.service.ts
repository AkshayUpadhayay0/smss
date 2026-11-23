import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { Schedule, ScheduleSlotRequest } from '../models/academic-setup.model';

const BASE = '/api/PeriodSchedules';

/** Always "my school's" bell schedules: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class PeriodScheduleService {
  private readonly api = inject(ApiService);

  list(): Observable<Schedule[]> {
    return this.api.get<Schedule[]>(BASE).pipe(map((rows) => rows ?? []));
  }

  create(scheduleName: string, copyFromScheduleId?: number | null): Observable<Schedule> {
    return this.api.post<Schedule>(BASE, { scheduleName, copyFromScheduleId: copyFromScheduleId ?? null });
  }

  rename(id: number, scheduleName: string): Observable<Schedule> {
    return this.api.put<Schedule>(`${BASE}/${id}`, { scheduleName });
  }

  setDefault(id: number): Observable<Schedule> {
    return this.api.post<Schedule>(`${BASE}/${id}/set-default`);
  }

  /** Refused (409) while the schedule is the default, assigned to a year, or has timetable entries on its periods. */
  remove(id: number): Observable<unknown> {
    return this.api.delete<unknown>(`${BASE}/${id}`);
  }

  addSlot(id: number, request: ScheduleSlotRequest): Observable<Schedule> {
    return this.api.post<Schedule>(`${BASE}/${id}/slots`, request);
  }

  updateSlot(id: number, periodId: number, request: ScheduleSlotRequest): Observable<Schedule> {
    return this.api.put<Schedule>(`${BASE}/${id}/slots/${periodId}`, request);
  }

  toggleSlot(id: number, periodId: number): Observable<Schedule> {
    return this.api.post<Schedule>(`${BASE}/${id}/slots/${periodId}/toggle-status`);
  }

  removeSlot(id: number, periodId: number): Observable<Schedule> {
    return this.api.delete<Schedule>(`${BASE}/${id}/slots/${periodId}`);
  }
}
