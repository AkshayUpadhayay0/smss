import { Routes } from '@angular/router';

// Mounted at the root: /transport/...  School-owned records (the school comes from the token on the API side).
export const TRANSPORT_ROUTES: Routes = [
  { path: 'transport', pathMatch: 'full', redirectTo: 'transport/areas' },
  { path: 'transport/areas', loadComponent: () => import('./pages/area-list/area-list.component').then((m) => m.AreaListComponent) },
  { path: 'transport/vehicles', loadComponent: () => import('./pages/vehicle-list/vehicle-list.component').then((m) => m.VehicleListComponent) },
  { path: 'transport/drivers', loadComponent: () => import('./pages/driver-list/driver-list.component').then((m) => m.DriverListComponent) },
];
