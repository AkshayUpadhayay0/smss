import { Routes } from '@angular/router';

// Mounted at the root: /calendar.
export const CALENDAR_ROUTES: Routes = [
  {
    path: 'calendar',
    loadComponent: () => import('./pages/calendar/calendar.component').then((m) => m.CalendarComponent),
  },
];
