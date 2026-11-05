import { Injectable, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { MonthlyAttendanceReport, ReportOptions } from '../models/attendance-report.model';
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

  // ---- monthly report (School Admin, Principal, and a class teacher for their own section; the server decides, 403 / 404 otherwise)
  /** The pickers of the report screen: academic years, and the sections this caller may report on in the chosen year (the current one when none is given). */
  reportOptions(academicYearId?: number): Observable<ReportOptions> {
    return this.api.get<ReportOptions>(`${BASE}/report/options`, academicYearId ? { academicYearId } : undefined);
  }

  monthlyReport(sectionId: number, year: number, month: number, academicYearId?: number): Observable<MonthlyAttendanceReport> {
    return this.api.get<MonthlyAttendanceReport>(`${BASE}/report/monthly`, { sectionId, year, month, ...(academicYearId ? { academicYearId } : {}) });
  }

  /** The same report as a print-ready A4 landscape PDF. A failure arrives as a JSON envelope inside the blob: its message is surfaced. */
  monthlyReportPdf(sectionId: number, year: number, month: number, academicYearId?: number): Observable<Blob> {
    const q = new URLSearchParams({ sectionId: String(sectionId), year: String(year), month: String(month), ...(academicYearId ? { academicYearId: String(academicYearId) } : {}) });
    return this.api.download(`${BASE}/report/monthly/pdf?${q.toString()}`, { skipErrorToast: true }).pipe(
      catchError((e: unknown) => from(readBlobError(e)).pipe(switchMap((message) => throwError(() => new Error(message))))),
    );
  }

  /** The whole section for today in one request; resubmitting the same day overwrites. */
  submit(request: SubmitAttendanceRequest): Observable<SubmitAttendanceResult> {
    return this.api.post<SubmitAttendanceResult>(`${BASE}/submit`, request);
  }
}

/** The `message` of a standard error envelope that arrived as a Blob, else a generic line. */
async function readBlobError(e: unknown): Promise<string> {
  const fallback = 'The report could not be generated. Please try again.';
  if (!(e instanceof HttpErrorResponse)) return fallback;
  try {
    const body: unknown = e.error instanceof Blob ? JSON.parse(await e.error.text()) : e.error;
    const message = (body as { message?: unknown } | null)?.message;
    if (typeof message === 'string' && message) return message;
  } catch {
    /* not JSON: fall through */
  }
  return e.status === 0 ? 'Cannot reach the server. Check your connection and try again.' : fallback;
}
