import { Routes } from '@angular/router';

// Mounted at the root: /promotion. School Admin and Principal open it (ROUTE_ROLES); the API re-checks.
export const PROMOTION_ROUTES: Routes = [
  { path: 'promotion', loadComponent: () => import('./pages/promotion/promotion.component').then((m) => m.PromotionComponent) },
  { path: 'promotion/pending', loadComponent: () => import('./pages/pending-promotion/pending-promotion.component').then((m) => m.PendingPromotionComponent) },
];
