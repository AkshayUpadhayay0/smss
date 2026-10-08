import { Routes } from '@angular/router';

const form = () => import('./pages/school-form/school-form.component').then((m) => m.SchoolFormComponent);

// Mounted at the root. `mode` and `schoolId` reach SchoolFormComponent via component input binding.
export const SCHOOLS_ROUTES: Routes = [
  {
    path: 'schools',
    loadComponent: () => import('./pages/school-list/school-list.component').then((m) => m.SchoolListComponent),
  },
  { path: 'schools/add', loadComponent: form, data: { mode: 'add' } },
  { path: 'schools/:schoolId/edit', loadComponent: form, data: { mode: 'edit' } },
  { path: 'schools/:schoolId/view', loadComponent: form, data: { mode: 'view' } },
];
