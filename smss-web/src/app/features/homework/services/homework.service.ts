import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { CreateHomeworkRequest, Homework, HomeworkFilters, UpdateHomeworkRequest } from '../models/homework.model';

const BASE = '/api/Homework';

/** The school and the acting user come from the token: nothing about them is ever sent. */
@Injectable({ providedIn: 'root' })
export class HomeworkService {
  private readonly api = inject(ApiService);

  /** A School Admin: the whole school, filterable. */
  list(filters: HomeworkFilters): Observable<Homework[]> {
    return this.api.get<Homework[]>(BASE, this.params(filters)).pipe(map((l) => l ?? []));
  }

  /** A Teacher: the homework they set. */
  my(filters: HomeworkFilters): Observable<Homework[]> {
    return this.api.get<Homework[]>(`${BASE}/my`, this.params(filters)).pipe(map((l) => l ?? []));
  }

  /** A Student: their own current section's homework, every subject. */
  studentMy(filters: Pick<HomeworkFilters, 'from' | 'to'> = {}): Observable<Homework[]> {
    return this.api.get<Homework[]>(`${BASE}/student/my`, this.params(filters)).pipe(map((l) => l ?? []));
  }

  create(request: CreateHomeworkRequest): Observable<Homework> {
    return this.api.post<Homework>(BASE, request);
  }

  update(id: number, request: UpdateHomeworkRequest): Observable<Homework> {
    return this.api.put<Homework>(`${BASE}/${id}`, request);
  }

  remove(id: number): Observable<Homework> {
    return this.api.delete<Homework>(`${BASE}/${id}`);
  }

  /** One attachment per homework: sending another replaces it. */
  uploadAttachment(id: number, file: File): Observable<Homework> {
    return this.api.upload<Homework>(`${BASE}/${id}/attachment`, file);
  }

  removeAttachment(id: number): Observable<Homework> {
    return this.api.post<Homework>(`${BASE}/${id}/attachment/remove`);
  }

  downloadAttachment(id: number): Observable<Blob> {
    return this.api.download(`${BASE}/attachment/${id}`);
  }

  private params(filters: HomeworkFilters): Record<string, string | number> {
    const params: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(filters)) if (value !== undefined && value !== null && value !== '') params[key] = value as string | number;
    return params;
  }
}
