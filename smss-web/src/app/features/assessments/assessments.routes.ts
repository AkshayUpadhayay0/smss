import { Routes } from '@angular/router';

// Mounted at the root: /assessments (Teacher, School Admin) and /assessments/my (Student).
export const ASSESSMENTS_ROUTES: Routes = [
  { path: 'assessments/my', loadComponent: () => import('./pages/my-results/my-results.component').then((m) => m.MyResultsComponent) },
  { path: 'assessments', loadComponent: () => import('./pages/assessment-list/assessment-list.component').then((m) => m.AssessmentListComponent) },
];
