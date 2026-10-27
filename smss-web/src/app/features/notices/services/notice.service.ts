import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, tap } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { Notice, NoticeFilters, SaveNoticeRequest } from '../models/notice.model';

const BASE = '/api/Notice';

/** The school and the acting user come from the token. `unreadCount` is shared with the topbar bell. */
@Injectable({ providedIn: 'root' })
export class NoticeService {
  private readonly api = inject(ApiService);

  /** The signed-in user's unread notices; the bell badge and the My Notices page both read and refresh it. */
  readonly unreadCount = signal(0);

  /** School Admin / Principal: every notice of the school, filterable by audience and published date. */
  list(filters: NoticeFilters = {}): Observable<Notice[]> {
    return this.api.get<Notice[]>(BASE, this.params(filters)).pipe(map((l) => l ?? []));
  }

  /** The notices addressed to the signed-in user, newest first. */
  my(filters: NoticeFilters = {}): Observable<Notice[]> {
    return this.api.get<Notice[]>(`${BASE}/my`, this.params(filters)).pipe(map((l) => l ?? []));
  }

  /** Opening a notice marks it read for the caller (server side); the badge is refreshed afterwards. */
  open(id: number): Observable<Notice> {
    return this.api.get<Notice>(`${BASE}/${id}`).pipe(tap(() => this.refreshUnread()));
  }

  create(request: SaveNoticeRequest): Observable<Notice> {
    return this.api.post<Notice>(BASE, request);
  }

  update(id: number, request: SaveNoticeRequest): Observable<Notice> {
    return this.api.put<Notice>(`${BASE}/${id}`, request);
  }

  remove(id: number): Observable<Notice> {
    return this.api.delete<Notice>(`${BASE}/${id}`);
  }

  refreshUnread(): void {
    this.api.get<{ count: number }>(`${BASE}/unread-count`).subscribe({ next: (r) => this.unreadCount.set(r?.count ?? 0), error: () => undefined });
  }

  private params(filters: NoticeFilters): Record<string, string | number | boolean> {
    const params: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== null && value !== '' && value !== false) params[key] = value as string | number | boolean;
    return params;
  }
}
