import { Routes } from '@angular/router';

// Mounted at the root inside the signed-in layout. Super Admin only: see '/enquiries' in ROUTE_ROLES.
export const ENQUIRIES_ROUTES: Routes = [
  {
    path: 'enquiries',
    loadComponent: () => import('./pages/enquiry-list/enquiry-list.component').then((m) => m.EnquiryListComponent),
  },
];
