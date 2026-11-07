import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_ERROR_TOAST } from '../interceptors/error.interceptor';
import { ApiResponse } from '../models/api-response.model';

export interface ApiRequestOptions {
  /** Caller shows its own error UI; suppress the global error toast. */
  skipErrorToast?: boolean;
}

/**
 * Thin wrapper over HttpClient: prefixes `environment.apiUrl` and unwraps the ApiResponse<T> envelope.
 * Emits `data` on success; errors with the envelope when `status` is false.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  get<T>(path: string, params?: Record<string, string | number | boolean>, options?: ApiRequestOptions): Observable<T> {
    return this.unwrap(this.http.get<ApiResponse<T>>(this.url(path), { params, context: this.context(options) }));
  }

  /** A file from an authenticated endpoint (the token is attached by the interceptor): the raw bytes, not the ApiResponse envelope. */
  download(path: string, options?: ApiRequestOptions): Observable<Blob> {
    return this.http.get(this.url(path), { responseType: 'blob', context: this.context(options) });
  }

  /** A POST whose answer is a file (e.g. a bulk PDF), not the ApiResponse envelope. A failure arrives as an HttpErrorResponse whose `error` is a Blob holding the JSON envelope. */
  postForBlob(path: string, body?: unknown, options?: ApiRequestOptions): Observable<Blob> {
    return this.http.post(this.url(path), body ?? {}, { responseType: 'blob', context: this.context(options) });
  }

  post<T>(path: string, body?: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.unwrap(this.http.post<ApiResponse<T>>(this.url(path), body ?? {}, { context: this.context(options) }));
  }

  put<T>(path: string, body?: unknown): Observable<T> {
    return this.unwrap(this.http.put<ApiResponse<T>>(this.url(path), body ?? {}));
  }

  delete<T>(path: string): Observable<T> {
    return this.unwrap(this.http.delete<ApiResponse<T>>(this.url(path)));
  }

  /** Multipart upload, e.g. school logo (field name "file"); `extraFields` are sent as plain form fields next to the file. */
  upload<T>(path: string, file: File, field = 'file', options?: ApiRequestOptions, extraFields?: Record<string, string>): Observable<T> {
    const form = new FormData();
    for (const [name, value] of Object.entries(extraFields ?? {})) form.append(name, value);
    form.append(field, file);
    return this.unwrap(this.http.post<ApiResponse<T>>(this.url(path), form, { context: this.context(options) }));
  }

  /** Multipart upload whose answer is a file (e.g. an error report .xlsx), not the ApiResponse envelope. */
  uploadForBlob(path: string, file: File, field = 'file', options?: ApiRequestOptions): Observable<Blob> {
    const form = new FormData();
    form.append(field, file);
    return this.http.post(this.url(path), form, { responseType: 'blob', context: this.context(options) });
  }

  private context(options?: ApiRequestOptions): HttpContext {
    return new HttpContext().set(SKIP_ERROR_TOAST, !!options?.skipErrorToast);
  }

  private url(path: string): string {
    return `${this.baseUrl}${path.startsWith('/') ? '' : '/'}${path}`;
  }

  private unwrap<T>(source: Observable<ApiResponse<T>>): Observable<T> {
    return source.pipe(
      map((res) => {
        if (!res.status) throw res;
        return res.data as T;
      }),
    );
  }
}
