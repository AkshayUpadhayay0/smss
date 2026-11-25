import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { CopyPart, SaveYearClassSubjectsRequest, SaveYearOfferingRequest, SaveYearSchedulesRequest, YearClassSubjects, YearCopyLog, YearCopyResult, YearOffering, YearSchedules } from '../models/academic-setup.model';

const BASE = '/api/AcademicSetup';

/** Always "my school's" setup: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class AcademicSetupService {
  private readonly api = inject(ApiService);

  /** Every class and section master of the school, flagged with whether the given year offers it. */
  getOffering(academicYearId: number): Observable<YearOffering> {
    return this.api.get<YearOffering>(`${BASE}/years/${academicYearId}/classes`);
  }

  /** Replaces the year's WHOLE offered set; anything not listed is withdrawn (refused while it is in use). */
  saveOffering(academicYearId: number, request: SaveYearOfferingRequest): Observable<YearOffering> {
    return this.api.put<YearOffering>(`${BASE}/years/${academicYearId}/classes`, request);
  }

  /** S7: what copying configuration from another year WOULD do. Nothing is saved. */
  previewCopy(targetYearId: number, sourceYearId: number, parts: CopyPart[], feeIncreasePercent = 0): Observable<YearCopyResult> {
    return this.api.post<YearCopyResult>(`${BASE}/years/${targetYearId}/copy-from/${sourceYearId}/preview`, { parts, feeIncreasePercent });
  }

  /** S7: runs the copy (skip-if-exists: nothing existing is changed or deleted) and writes one log row. */
  applyCopy(targetYearId: number, sourceYearId: number, parts: CopyPart[], feeIncreasePercent = 0): Observable<YearCopyResult> {
    return this.api.post<YearCopyResult>(`${BASE}/years/${targetYearId}/copy-from/${sourceYearId}/apply`, { parts, confirm: true, feeIncreasePercent });
  }

  copyLog(targetYearId: number): Observable<YearCopyLog[]> {
    return this.api.get<YearCopyLog[]>(`${BASE}/years/${targetYearId}/copy-log`);
  }

  /** Which bell schedule each class follows on each day of the year (an empty list = everything follows the default schedule). */
  getYearSchedules(academicYearId: number): Observable<YearSchedules> {
    return this.api.get<YearSchedules>(`${BASE}/years/${academicYearId}/schedules`);
  }

  /** Replaces the year's WHOLE set of assignments (refused while it would leave a timetable slot on a schedule the class no longer follows). */
  saveYearSchedules(academicYearId: number, request: SaveYearSchedulesRequest): Observable<YearSchedules> {
    return this.api.put<YearSchedules>(`${BASE}/years/${academicYearId}/schedules`, request);
  }

  /** The subjects one class is taught in one year, with the per-section exceptions and what is in use. */
  getClassSubjects(academicYearId: number, classId: number): Observable<YearClassSubjects> {
    return this.api.get<YearClassSubjects>(`${BASE}/years/${academicYearId}/classes/${classId}/subjects`);
  }

  /** Replaces the class's WHOLE subject set for the year and its sections' exceptions (refused while a removed subject is in use). */
  saveClassSubjects(academicYearId: number, classId: number, request: SaveYearClassSubjectsRequest): Observable<YearClassSubjects> {
    return this.api.put<YearClassSubjects>(`${BASE}/years/${academicYearId}/classes/${classId}/subjects`, request);
  }
}
