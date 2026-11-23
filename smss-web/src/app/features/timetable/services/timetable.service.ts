import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import {
  ApplyResult,
  AssignSlotRequest,
  AssignTimetableRequest,
  Period,
  PeriodRequest,
  TimetableEntry,
  TimetableActivation,
  TimetableGrid,
  TimetableLookups,
  TimetableSummary,
  TimetableValidation,
  UnassignTimetableRequest,
} from '../models/timetable.model';

const PERIODS = '/api/Period';
const BASE = '/api/Timetable';
const VERSIONS = '/api/Timetables';

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
  lookups(sectionId: number, academicYearId?: number): Observable<TimetableLookups> {
    return this.api.get<TimetableLookups>(`${BASE}/lookups`, academicYearId != null ? { sectionId, academicYearId } : { sectionId });
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

  // ---- S6: timetable versions (several per section and year, one ACTIVE)
  versions(sectionId: number, academicYearId: number): Observable<TimetableSummary[]> {
    return this.api.get<TimetableSummary[]>(VERSIONS, { sectionId, academicYearId }).pipe(map((l) => l ?? []));
  }

  versionGrid(timetableId: number): Observable<TimetableGrid> {
    return this.api.get<TimetableGrid>(`${VERSIONS}/${timetableId}/grid`);
  }

  createVersion(sectionId: number, academicYearId: number, timetableName: string, cloneFromTimetableId?: number | null): Observable<TimetableSummary> {
    return this.api.post<TimetableSummary>(VERSIONS, { sectionId, academicYearId, timetableName, cloneFromTimetableId: cloneFromTimetableId ?? null });
  }

  renameVersion(timetableId: number, timetableName: string): Observable<TimetableSummary> {
    return this.api.post<TimetableSummary>(`${VERSIONS}/${timetableId}/rename`, { timetableName });
  }

  assignSlot(timetableId: number, request: AssignSlotRequest): Observable<TimetableEntry> {
    return this.api.post<TimetableEntry>(`${VERSIONS}/${timetableId}/slots/assign`, request);
  }

  unassignSlot(timetableId: number, dayOfWeek: number, periodId: number): Observable<TimetableEntry> {
    return this.api.post<TimetableEntry>(`${VERSIONS}/${timetableId}/slots/unassign`, { dayOfWeek, periodId });
  }

  /** Dry run: every problem, nothing saved. */
  validateVersion(timetableId: number): Observable<TimetableValidation> {
    return this.api.post<TimetableValidation>(`${VERSIONS}/${timetableId}/validate`);
  }

  activateVersion(timetableId: number): Observable<TimetableActivation> {
    return this.api.post<TimetableActivation>(`${VERSIONS}/${timetableId}/activate`);
  }

  archiveVersion(timetableId: number): Observable<TimetableSummary> {
    return this.api.post<TimetableSummary>(`${VERSIONS}/${timetableId}/archive`);
  }

  restoreVersion(timetableId: number): Observable<TimetableSummary> {
    return this.api.post<TimetableSummary>(`${VERSIONS}/${timetableId}/restore`);
  }

  deleteVersion(timetableId: number): Observable<unknown> {
    return this.api.delete<unknown>(`${VERSIONS}/${timetableId}`);
  }

  previewApply(timetableId: number, sectionIds: number[], timetableName: string | null): Observable<ApplyResult> {
    return this.api.post<ApplyResult>(`${VERSIONS}/${timetableId}/apply-preview`, { sectionIds, timetableName });
  }

  apply(timetableId: number, sectionIds: number[], timetableName: string | null): Observable<ApplyResult> {
    return this.api.post<ApplyResult>(`${VERSIONS}/${timetableId}/apply`, { sectionIds, timetableName });
  }
}
