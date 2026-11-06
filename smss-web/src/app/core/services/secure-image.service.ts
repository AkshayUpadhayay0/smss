import { Injectable, inject } from '@angular/core';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';
import { ApiService } from './api.service';

/**
 * Images that need the login (a student photo is private: GET /api/Student/{id}/photo) cannot be an <img src>, because an img tag cannot carry the
 * Authorization header. This fetches them through the normal authenticated API client and hands out an object URL.
 *
 * - One in-memory cache entry per API path (the path carries a ?v= fingerprint, so a replaced photo is a new entry).
 * - Requests for the same path that are in flight at the same time share ONE request.
 * - The cache is bounded: when it is full the oldest object URLs are revoked. clear() revokes everything (called on sign-out).
 * - Nothing is written to localStorage / sessionStorage / the Cache API: closing the tab discards it.
 */
@Injectable({ providedIn: 'root' })
export class SecureImageService {
  static readonly MAX_ENTRIES = 300;
  private readonly api = inject(ApiService);
  private readonly cache = new Map<string, string>();      // api path -> object URL (insertion order = age)
  private readonly inflight = new Map<string, Observable<string | null>>();

  /** True for a path that must be fetched with the login. */
  static isSecurePath(src: string | null | undefined): boolean { return !!src && src.startsWith('/api/'); }

  /** The object URL already held for this path, or null. */
  peek(path: string): string | null { return this.cache.get(path) ?? null; }

  /** The object URL for an API path, or null when the image could not be loaded (no photo, not allowed, offline). Never throws, never shows an error toast. */
  get(path: string): Observable<string | null> {
    const hit = this.cache.get(path);
    if (hit) return of(hit);
    const running = this.inflight.get(path);
    if (running) return running;
    const request$ = this.api.download(path, { skipErrorToast: true }).pipe(
      map((blob) => URL.createObjectURL(blob)),
      tap((url) => this.remember(path, url)),
      catchError(() => of(null)),
      finalize(() => this.inflight.delete(path)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.inflight.set(path, request$);
    return request$;
  }

  /** Revokes every object URL and forgets everything (sign-out). */
  clear(): void {
    for (const url of this.cache.values()) URL.revokeObjectURL(url);
    this.cache.clear();
    this.inflight.clear();
  }

  get size(): number { return this.cache.size; }

  private remember(path: string, url: string): void {
    this.cache.set(path, url);
    while (this.cache.size > SecureImageService.MAX_ENTRIES) {
      const oldest = this.cache.keys().next().value as string;
      URL.revokeObjectURL(this.cache.get(oldest)!);
      this.cache.delete(oldest);
    }
  }
}
