import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { SaveYearOfferingRequest, YearOffering } from '../models/academic-setup.model';

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
}
