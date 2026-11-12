import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { AdmissionDashboard } from '../models/admission-dashboard.model';

/** Admission Staff only: the server resolves the school from the token, so no id is sent. */
@Injectable({ providedIn: 'root' })
export class AdmissionDashboardService {
  private readonly api = inject(ApiService);

  summary(): Observable<AdmissionDashboard> {
    return this.api.get<AdmissionDashboard>('/api/AdmissionDashboard/summary');
  }
}
