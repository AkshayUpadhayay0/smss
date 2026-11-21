import { HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OnboardingService } from '../services/onboarding.service';

/** The APIs behind the setup steps: a successful save here can change the setup status. */
const SETUP_API_PREFIXES = ['/api/AcademicYear', '/api/Classes', '/api/Sections', '/api/MasterImport', '/api/FeeType', '/api/FeeStructure'];

export function isSetupApiWrite(method: string, url: string): boolean {
  if (method === 'GET' || method === 'HEAD') return false;
  const path = url.startsWith(environment.apiUrl) ? url.slice(environment.apiUrl.length) : url;
  return SETUP_API_PREFIXES.some((p) => path === p || path.startsWith(p + '/') || path.startsWith(p + '?'));
}

/**
 * After a successful save to a setup API (academic year, classes, sections, import masters, fee types, fee structures) the setup status
 * is read again, so the menu unlocks (or the checklist ticks a step) at once. Fire and forget; a failed read keeps the last answer.
 */
export const setupRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  const watch = isSetupApiWrite(req.method, req.url);
  return next(req).pipe(
    tap((event) => {
      if (watch && event.type === HttpEventType.Response && event.status >= 200 && event.status < 300) {
        injector.get(OnboardingService).refresh().subscribe();
      }
    }),
  );
};
