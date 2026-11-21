import { OnboardingStatus, OnboardingStep } from '../models/onboarding.model';

const STEPS: Omit<OnboardingStep, 'done' | 'detail'>[] = [
  { code: 'PASSWORD_CHANGED', title: 'Change your temporary password', route: '/change-password' },
  { code: 'ACADEMIC_YEAR', title: 'Add the academic year', route: '/school-setup/academic-year' },
  { code: 'IMPORT_MASTERS_OR_CLASSES', title: 'Import masters or add classes', route: '/school-setup/import-masters' },
  { code: 'SCHOOL_SETUP', title: 'Complete school setup', route: '/school-setup/sections' },
  { code: 'FEE_TYPES', title: 'Add fee types', route: '/fee-setup/fee-types' },
  { code: 'FEE_STRUCTURE', title: 'Add a fee structure for every class', route: '/fee-setup/fee-structures' },
];

/** A status where the first `doneCount` steps are done. 6 = every step done (complete). */
export function makeStatus(doneCount: number, overrides: Partial<OnboardingStatus> = {}): OnboardingStatus {
  const steps = STEPS.map((s, i) => ({ ...s, done: i < doneCount, detail: i < doneCount ? 'Done.' : 'Still to do.' }));
  const next = steps.find((s) => !s.done) ?? null;
  return { isComplete: next === null, nextStep: next, steps, ...overrides };
}

export const envelope = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
