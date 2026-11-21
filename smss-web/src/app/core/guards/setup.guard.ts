import { inject } from '@angular/core';
import { CanActivateChildFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { isSetupGated } from '../config/setup-gate';
import { AuthService } from '../services/auth.service';
import { OnboardingService } from '../services/onboarding.service';
import { ToastService } from '../services/toast.service';

/**
 * Keeps a School Admin whose school setup is unfinished out of the screens that add students, employees and transport
 * (SETUP_GATED_PREFIXES) and sends them to the next setup step with a message. Everything else passes untouched.
 *
 * Convenience only: the API refuses the same creates with 409 SETUP_INCOMPLETE. If the status cannot be read the page opens
 * (the API will still refuse a save), so a status hiccup never locks anyone out of the app.
 * A first-login user is left to roleGuard (the password screen comes first).
 */
export const setupGuard: CanActivateChildFn = (_route, state) => {
  const auth = inject(AuthService);
  const onboarding = inject(OnboardingService);
  const router = inject(Router);
  const toast = inject(ToastService);

  if (!auth.isLoggedIn() || auth.user()?.isFirstLogin) return true;
  if (!onboarding.isSchoolAdmin() || !isSetupGated(state.url)) return true;

  return onboarding.ensure().pipe(
    map((status) => {
      if (!status || status.isComplete || !status.nextStep) return true;
      toast.warning(`Finish the school setup first. Next step: ${status.nextStep.title}.`, 'Setup incomplete');
      return router.createUrlTree([status.nextStep.route]);
    }),
  );
};
