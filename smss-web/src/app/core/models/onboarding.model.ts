/** One step of the first-login setup checklist (GET /api/Onboarding/status). Derived on the server from existing data. */
export interface OnboardingStep {
  code: string;
  title: string;
  done: boolean;
  /** The screen that completes this step. */
  route: string;
  /** A short, safe sentence about what is still missing (or what is done). */
  detail: string;
}

export interface OnboardingStatus {
  /** True when every step is done, or the school already has an active student. */
  isComplete: boolean;
  /** The first step not done; null when complete. */
  nextStep: OnboardingStep | null;
  steps: OnboardingStep[];
}

/** The stable code the API puts in `data.code` of a 409 when a student / employee / transport record is added before setup is complete. */
export const SETUP_INCOMPLETE = 'SETUP_INCOMPLETE';
