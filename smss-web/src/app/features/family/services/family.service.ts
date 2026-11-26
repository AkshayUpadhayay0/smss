import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { MyReportCard, MyReportCardDetail } from '../../examinations/models/results.model';
import { FeeReceipt } from '../../students/models/fee-payment.model';
import { StudentLeave } from '../../leave/models/leave.model';
import { StudentMyFees } from '../../students/models/student.model';
import { TimetableGrid } from '../../timetable/models/timetable.model';
import {
  FamilyAttendance, FamilyChildProfile, FamilyChildren, FamilyEvent, FamilyExamSchedule, FamilyHomework, FamilyNotice,
} from '../models/family.model';

const BASE = '/api/Family';
const enc = encodeURIComponent;

/**
 * The parent's read-only API. Every call is a GET on api/Family and carries NO identity: the guardian and the school come from the token, and a child id in the path is checked by the server against the links.
 * Nothing else is ever requested by the parent screens.
 */
@Injectable({ providedIn: 'root' })
export class FamilyService {
  private readonly api = inject(ApiService);

  /** The unread-notice count shown on the navigation badge; null until it has been read (or when it could not be). */
  readonly unread = signal<number | null>(null);
  refreshUnread(): void { this.unreadNotices().subscribe({ next: (r) => this.unread.set(r.count), error: () => this.unread.set(null) }); }

  children(): Observable<FamilyChildren> { return this.api.get<FamilyChildren>(`${BASE}/children`, undefined, { skipErrorToast: true }); }
  profile(id: string): Observable<FamilyChildProfile> { return this.api.get<FamilyChildProfile>(`${BASE}/children/${enc(id)}/profile`, undefined, { skipErrorToast: true }); }
  attendance(id: string, from: string, to: string): Observable<FamilyAttendance> { return this.api.get<FamilyAttendance>(`${BASE}/children/${enc(id)}/attendance`, { from, to }, { skipErrorToast: true }); }
  homework(id: string): Observable<FamilyHomework[]> { return this.api.get<FamilyHomework[]>(`${BASE}/children/${enc(id)}/homework`, undefined, { skipErrorToast: true }); }
  timetable(id: string): Observable<TimetableGrid> { return this.api.get<TimetableGrid>(`${BASE}/children/${enc(id)}/timetable`, undefined, { skipErrorToast: true }); }
  examSchedule(id: string): Observable<FamilyExamSchedule[]> { return this.api.get<FamilyExamSchedule[]>(`${BASE}/children/${enc(id)}/exam-schedule`, undefined, { skipErrorToast: true }); }
  reportCards(id: string): Observable<MyReportCard[]> { return this.api.get<MyReportCard[]>(`${BASE}/children/${enc(id)}/report-cards`, undefined, { skipErrorToast: true }); }
  reportCard(id: string, publicationId: number): Observable<MyReportCardDetail> { return this.api.get<MyReportCardDetail>(`${BASE}/children/${enc(id)}/report-cards/${publicationId}`, undefined, { skipErrorToast: true }); }
  reportCardPdf(id: string, publicationId: number): Observable<Blob> { return this.api.download(`${BASE}/children/${enc(id)}/report-cards/${publicationId}/pdf`, { skipErrorToast: true }); }
  fees(id: string): Observable<StudentMyFees> { return this.api.get<StudentMyFees>(`${BASE}/children/${enc(id)}/fees`, undefined, { skipErrorToast: true }); }
  receipt(id: string, paymentId: number): Observable<FeeReceipt> { return this.api.get<FeeReceipt>(`${BASE}/children/${enc(id)}/receipts/${paymentId}`, undefined, { skipErrorToast: true }); }
  leave(id: string): Observable<StudentLeave[]> { return this.api.get<StudentLeave[]>(`${BASE}/children/${enc(id)}/leave`, undefined, { skipErrorToast: true }); }

  notices(unreadOnly = false): Observable<FamilyNotice[]> { return this.api.get<FamilyNotice[]>(`${BASE}/notices`, unreadOnly ? { unreadOnly: true } : undefined, { skipErrorToast: true }); }
  unreadNotices(): Observable<{ count: number }> { return this.api.get<{ count: number }>(`${BASE}/notices/unread-count`, undefined, { skipErrorToast: true }); }
  /** Opening a notice marks it read. */
  notice(id: number): Observable<FamilyNotice> { return this.api.get<FamilyNotice>(`${BASE}/notices/${id}`, undefined, { skipErrorToast: true }); }
  calendar(from: string, to: string): Observable<FamilyEvent[]> { return this.api.get<FamilyEvent[]>(`${BASE}/calendar`, { from, to }, { skipErrorToast: true }); }
}
