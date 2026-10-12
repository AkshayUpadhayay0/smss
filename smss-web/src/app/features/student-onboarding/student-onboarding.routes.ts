import { Routes } from '@angular/router';

// Mounted at the root. There is no Student module yet, so this sits beside the other School Admin screens;
// when /students/* pages exist this route already lives in the right place.
export const STUDENT_ONBOARDING_ROUTES: Routes = [
  {
    path: 'students/onboard-existing',
    loadComponent: () => import('./pages/existing-student-form/existing-student-form.component').then((m) => m.ExistingStudentFormComponent),
  },
];
