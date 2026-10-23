import { Routes } from '@angular/router';

// Mounted at the root: /timetable, /timetable/my and /school-setup/periods.
export const TIMETABLE_ROUTES: Routes = [
  {
    path: 'school-setup/periods',
    loadComponent: () => import('./pages/period-list/period-list.component').then((m) => m.PeriodListComponent),
  },
  {
    path: 'timetable/my',
    loadComponent: () => import('./pages/my-timetable/my-timetable.component').then((m) => m.MyTimetableComponent),
  },
  {
    path: 'timetable',
    loadComponent: () => import('./pages/timetable-admin/timetable-admin.component').then((m) => m.TimetableAdminComponent),
  },
];
