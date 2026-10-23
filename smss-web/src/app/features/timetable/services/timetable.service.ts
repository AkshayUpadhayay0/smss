import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  AssignTimetableRequest,
  Period,
  PeriodRequest,
  TimetableEntry,
  TimetableGrid,
  TimetableLookups,
  UnassignTimetableRequest,
} from '../models/timetable.model';

const PERIODS = '/api/Period';
const BASE = '/api/Timetable';

/** The school and the acting user come from the token: nothing about them is ever sent. */
@Injectable({ providedIn: 'root' })
export class TimetableService {
  private readonly api = inject(ApiService);

  // ---- bell schedule
  periods(): Observable<Period[]> {
    return this.api.get<Period[]>(PERIODS).pipe(map((l) => l ?? []));
  }

  createPeriod(request: PeriodRequest): Observable<Period> {
    return this.api.post<Period>(PERIODS, request);
  }

  updatePeriod(id: number, request: PeriodRequest): Observable<Period> {
    return this.api.put<Period>(`${PERIODS}/${id}`, request);
  }

  togglePeriod(id: number): Observable<Period> {
    return this.api.post<Period>(`${PERIODS}/${id}/toggle-status`, {});
  }

  // ---- timetable
  lookups(sectionId: number): Observable<TimetableLookups> {
    return this.api.get<TimetableLookups>(`${BASE}/lookups`, { sectionId });
  }

  classGrid(sectionId: number, academicYearId?: number): Observable<TimetableGrid> {
    return this.api.get<TimetableGrid>(`${BASE}/class/${sectionId}`, academicYearId ? { academicYearId } : undefined);
  }

  /** The signed-in teacher's own weekly timetable. */
  myGrid(academicYearId?: number): Observable<TimetableGrid> {
    return this.api.get<TimetableGrid>(`${BASE}/my`, academicYearId ? { academicYearId } : undefined);
  }

  assign(request: AssignTimetableRequest): Observable<TimetableEntry> {
    return this.api.post<TimetableEntry>(`${BASE}/assign`, request);
  }

  unassign(request: UnassignTimetableRequest): Observable<TimetableEntry> {
    return this.api.post<TimetableEntry>(`${BASE}/unassign`, request);
  }
}
