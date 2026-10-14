import { Routes } from '@angular/router';

// Mounted at the root: /fee-setup/... (Fee Types lives with the other simple masters in school-setup.routes).
export const FEE_SETUP_ROUTES: Routes = [
  {
    path: 'fee-setup/fee-concessions',
    loadComponent: () => import('./pages/fee-concession-list/fee-concession-list.component').then((m) => m.FeeConcessionListComponent),
  },
  {
    path: 'fee-setup/fee-structures',
    loadComponent: () => import('./pages/fee-structure-list/fee-structure-list.component').then((m) => m.FeeStructureListComponent),
  },
  {
    path: 'fee-setup/fee-structures/new',
    loadComponent: () => import('./pages/fee-structure-builder/fee-structure-builder.component').then((m) => m.FeeStructureBuilderComponent),
  },
  {
    // :feeStructureId is bound to the builder's `feeStructureId` input (withComponentInputBinding)
    path: 'fee-setup/fee-structures/:feeStructureId/edit',
    loadComponent: () => import('./pages/fee-structure-builder/fee-structure-builder.component').then((m) => m.FeeStructureBuilderComponent),
  },
];
