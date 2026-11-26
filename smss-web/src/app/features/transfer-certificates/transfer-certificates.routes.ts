import { Routes } from '@angular/router';

// Mounted at the root: /transfer-certificates. School Admin and Principal open it (ROUTE_ROLES); the API re-checks.
export const TRANSFER_CERTIFICATE_ROUTES: Routes = [
  { path: 'transfer-certificates', loadComponent: () => import('./pages/transfer-certificates/transfer-certificates.component').then((m) => m.TransferCertificatesComponent) },
];
