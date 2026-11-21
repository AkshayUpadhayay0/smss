import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ApiService } from '../../../core/services/api.service';
import { PartnerSchool } from '../models/partner-school.model';

@Injectable({ providedIn: 'root' })
export class PartnerSchoolService {
  private readonly api = inject(ApiService);

  /** Anonymous. The section hides itself on any failure, so the global error toast is skipped. */
  getPartnerSchools(): Observable<PartnerSchool[]> {
    return this.api.get<PartnerSchool[]>('/api/public/partner-schools', undefined, { skipErrorToast: true }).pipe(
      map((list) => (Array.isArray(list) ? list.map((s) => ({ ...s, logoUrl: toAbsoluteLogo(s.logoUrl) })) : [])),
    );
  }
}

/** The API returns a relative /uploads/... path; make it absolute for <img src>. */
export function toAbsoluteLogo(url: string | null | undefined): string | null {
  if (!url) return null;
  return /^https?:\/\//i.test(url) ? url : `${environment.apiUrl}${url}`;
}
