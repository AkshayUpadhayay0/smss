import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, switchMap, throwError } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { ImportPreview, ImportResult } from '../models/student-import.model';

const BASE = '/api/StudentImport';

/** Student Excel import. The school always comes from the token. Preview and commit both take the file: the server re-checks it at commit. */
@Injectable({ providedIn: 'root' })
export class StudentImportService {
  private readonly api = inject(ApiService);

  template(): Observable<Blob> {
    return this.api.download(`${BASE}/template`, { skipErrorToast: true }).pipe(catchError(rethrowBlobError));
  }

  preview(file: File): Observable<ImportPreview> {
    return this.api.upload<ImportPreview>(`${BASE}/preview`, file);
  }

  commit(file: File): Observable<ImportResult> {
    return this.api.upload<ImportResult>(`${BASE}/commit`, file);
  }

  errorReport(file: File): Observable<Blob> {
    return this.api.uploadForBlob(`${BASE}/error-report`, file, 'file', { skipErrorToast: true }).pipe(catchError(rethrowBlobError));
  }
}

/** A file endpoint's error arrives as a Blob; read the standard envelope's message out of it. */
function rethrowBlobError(e: unknown): Observable<never> {
  return from(readBlobError(e)).pipe(switchMap((message) => throwError(() => new Error(message))));
}

async function readBlobError(e: unknown): Promise<string> {
  const generic = 'The file could not be prepared. Please try again.';
  if (!(e instanceof HttpErrorResponse)) return generic;
  try {
    const body: unknown = e.error instanceof Blob ? JSON.parse(await e.error.text()) : e.error;
    const message = (body as { message?: unknown } | null)?.message;
    return typeof message === 'string' && message ? message : generic;
  } catch {
    return generic;
  }
}
