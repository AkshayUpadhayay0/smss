import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap } from 'rxjs';
import { SETUP_STATUS_ROLES, isSetupGated } from '../config/setup-gate';
import { ROLES } from '../config/route-roles';
import { OnboardingStatus, OnboardingStep, SETUP_INCOMPLETE } from '../models/onboarding.model';
import { ApiResponse } from '../models/api-response.model';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { ConfirmDialogService } from './confirm-dialog.service';
import { ToastService } from './toast.service';

const URL = '/api/Onboarding/status';

/**
 * The first-login setup checklist, as the API derives it. Held per signed-in user: another login never sees someone else's answer.
 * Only a School Admin, Principal or Accountant has a status (the API refuses everyone else), and nothing is requested before the
 * temporary password has been replaced (the API refuses that too).
 */
@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  private readonly stored = signal<{ userId: number; status: OnboardingStatus } | null>(null);
  private inflight$: Observable<OnboardingStatus | null> | null = null;

  /** Whether this login has a setup status at all. */
  readonly eligible = computed(() => {
    const user = this.auth.user();
    return !!user && !user.isFirstLogin && this.auth.currentUserRoles().some((r) => SETUP_STATUS_ROLES.includes(r));
  });
  readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().includes(ROLES.SCHOOL_ADMIN));

  /** The last answer for the signed-in user; null before the first answer. */
  readonly status = computed<OnboardingStatus | null>(() => {
    const s = this.stored();
    return s && s.userId === this.auth.user()?.userId ? s.status : null;
  });
  /** True only when the status is KNOWN to be incomplete (an unknown status never locks anything: the API is the real gate). */
  readonly incomplete = computed(() => this.eligible() && !!this.status() && !this.status()!.isComplete);
  readonly nextStep = computed<OnboardingStep | null>(() => (this.incomplete() ? this.status()!.nextStep : null));

  /** Whether the menu entry / route is closed right now. */
  isLocked(route: string): boolean {
    return this.incomplete() && isSetupGated(route);
  }

  /** Asks the API again (single flight). Emits null when the status is not available (not eligible, or the request failed) and keeps the last known answer. */
  refresh(): Observable<OnboardingStatus | null> {
    if (!this.eligible()) return of(null);
    if (!this.inflight$) {
      const userId = this.auth.user()!.userId;
      this.inflight$ = this.api.get<OnboardingStatus>(URL, undefined, { skipErrorToast: true }).pipe(
        tap((status) => this.stored.set({ userId, status })),
        catchError(() => of(null)),
        finalize(() => (this.inflight$ = null)),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.inflight$;
  }

  /**
   * The status for a decision: a known COMPLETE status is trusted (it does not go backwards in normal use, and the API still checks every create),
   * anything else is asked again so a step finished in another tab is noticed.
   */
  ensure(): Observable<OnboardingStatus | null> {
    const known = this.status();
    return known?.isComplete ? of(known) : this.refresh();
  }

  /** Where a School Admin lands after signing in or changing the password: the next setup step, or `fallback` when setup is complete or unknown. */
  landingFor(fallback: string): Observable<string> {
    if (!this.isSchoolAdmin()) return of(fallback);
    return this.refresh().pipe(map((s) => (s && !s.isComplete && s.nextStep ? s.nextStep.route : fallback)));
  }

  /** A 409 SETUP_INCOMPLETE from any action: a friendly explanation and a button to the next step. Never the raw error. */
  async handleSetupIncomplete(error: HttpErrorResponse): Promise<void> {
    const next = nextStepOf(error);
    this.refresh().subscribe();
    if (!this.isSchoolAdmin() || !next) {
      this.toast.warning('Your school setup is not finished yet. Ask your School Admin to complete it.', 'Setup incomplete');
      return;
    }
    const go = await this.confirmDialog.confirm({
      title: 'Finish your school setup first',
      message: `You can add students, employees and transport once the school setup is complete. Next step: ${next.title}.`,
      confirmText: 'Go to next step',
      cancelText: 'Not now',
    });
    if (go) void this.router.navigateByUrl(next.route);
  }
}

/** True for the API's 409 SETUP_INCOMPLETE answer. */
export function isSetupIncomplete(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && error.status === 409 && codeOf(error) === SETUP_INCOMPLETE;
}

function codeOf(error: HttpErrorResponse): string | undefined {
  return (error.error as Partial<ApiResponse<{ code?: string }>> | null)?.data?.code;
}

function nextStepOf(error: HttpErrorResponse): OnboardingStep | null {
  const step = (error.error as Partial<ApiResponse<{ nextStep?: OnboardingStep | null }>> | null)?.data?.nextStep;
  // only a screen inside this app: never navigate to whatever a response says
  return step && typeof step.route === 'string' && step.route.startsWith('/') && !step.route.startsWith('//') ? step : null;
}
