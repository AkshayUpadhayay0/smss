import { Routes } from '@angular/router';

// Mounted at the root: /admissions, /admissions/new, /admissions/:admissionId
// ('new' is listed before ':admissionId' so it is never read as an id)
export const ADMISSIONS_ROUTES: Routes = [
  {
    path: 'admissions',
    loadComponent: () => import('./pages/admission-list/admission-list.component').then((m) => m.AdmissionListComponent),
  },
  {
    path: 'admissions/new',
    loadComponent: () => import('./pages/admission-form/admission-form.component').then((m) => m.AdmissionFormComponent),
  },
  {
    path: 'admissions/:admissionId/edit',
    loadComponent: () => import('./pages/admission-form/admission-form.component').then((m) => m.AdmissionFormComponent),
  },
  {
    path: 'admissions/:admissionId',
    loadComponent: () => import('./pages/admission-detail/admission-detail.component').then((m) => m.AdmissionDetailComponent),
  },
];
