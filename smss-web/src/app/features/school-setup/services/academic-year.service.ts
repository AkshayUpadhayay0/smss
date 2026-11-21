import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AcademicYear, AcademicYearRequest } from '../models/academic-year.model';

const BASE = '/api/AcademicYear';

/** Always "my school's" years: the server resolves the school from the token, so no school id is ever sent. */
@Injectable({ providedIn: 'root' })
export class AcademicYearService {
  private readonly api = inject(ApiService);

  list(): Observable<AcademicYear[]> {
    return this.api.get<AcademicYear[]>(BASE).pipe(map((years) => years ?? []));
  }

  create(request: AcademicYearRequest): Observable<AcademicYear> {
    return this.api.post<AcademicYear>(BASE, request);
  }

  update(id: number, request: AcademicYearRequest): Observable<AcademicYear> {
    return this.api.put<AcademicYear>(`${BASE}/${id}`, request);
  }

  toggleStatus(id: number): Observable<AcademicYear> {
    return this.api.post<AcademicYear>(`${BASE}/${id}/toggle-status`);
  }

  /** Closing blocks configuration, attendance, enrolments, homework and timetable edits; fee payments and reads stay open. The current year can't be closed. */
  close(id: number, note?: string): Observable<AcademicYear> {
    return this.api.post<AcademicYear>(`${BASE}/${id}/close`, { note: note ?? null });
  }

  reopen(id: number): Observable<AcademicYear> {
    return this.api.post<AcademicYear>(`${BASE}/${id}/reopen`);
  }

  /** Makes this the current year and clears the flag on every other year, in one server transaction. */
  setCurrent(id: number): Observable<AcademicYear> {
    return this.api.post<AcademicYear>(`${BASE}/${id}/set-current`);
  }
}
