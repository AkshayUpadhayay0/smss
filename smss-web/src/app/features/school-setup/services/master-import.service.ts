import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { MasterImportCatalog, MasterImportRequest, MasterImportResult } from '../models/master-import.model';

const BASE = '/api/MasterImport';

/** Always the signed-in user's own school: the server reads it from the token, so no school id is sent. */
@Injectable({ providedIn: 'root' })
export class MasterImportService {
  private readonly api = inject(ApiService);

  getCatalog(): Observable<MasterImportCatalog> {
    return this.api.get<MasterImportCatalog>(`${BASE}/catalog`);
  }

  import(request: MasterImportRequest): Observable<MasterImportResult> {
    return this.api.post<MasterImportResult>(`${BASE}/import`, request);
  }
}
