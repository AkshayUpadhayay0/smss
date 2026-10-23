import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { EventCategory, SchoolEvent, SchoolEventRequest } from '../models/calendar.model';

const BASE = '/api/SchoolEvent';

/** Always "my school's" events: the server takes the school from the token. */
@Injectable({ providedIn: 'root' })
export class SchoolEventService {
  private readonly api = inject(ApiService);

  /** Events overlapping [from, to] (yyyy-MM-dd), for the month grid. */
  range(from: string, to: string): Observable<SchoolEvent[]> {
    return this.api.get<SchoolEvent[]>(BASE, { from, to }).pipe(map((l) => l ?? []));
  }

  upcoming(take = 5): Observable<SchoolEvent[]> {
    return this.api.get<SchoolEvent[]>(`${BASE}/upcoming`, { take }).pipe(map((l) => l ?? []));
  }

  categories(): Observable<EventCategory[]> {
    return this.api.get<EventCategory[]>(`${BASE}/categories`).pipe(map((l) => l ?? []));
  }

  create(request: SchoolEventRequest): Observable<SchoolEvent> {
    return this.api.post<SchoolEvent>(BASE, request);
  }

  update(eventId: number, request: SchoolEventRequest): Observable<SchoolEvent> {
    return this.api.put<SchoolEvent>(`${BASE}/${eventId}`, request);
  }

  /** Soft delete: the event is hidden, never removed. */
  delete(eventId: number): Observable<SchoolEvent> {
    return this.api.delete<SchoolEvent>(`${BASE}/${eventId}`);
  }
}
