import { Routes } from '@angular/router';

// Mounted at the root: /school-profile
export const SCHOOL_PROFILE_ROUTES: Routes = [
  {
    path: 'school-profile',
    loadComponent: () => import('./pages/school-profile/school-profile.component').then((m) => m.SchoolProfileComponent),
  },
];
