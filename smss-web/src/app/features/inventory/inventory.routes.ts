import { Routes } from '@angular/router';

// Mounted at the root: /inventory (Dashboard), /inventory/items, /inventory/add-stock, /inventory/adjustments, /inventory/issues, /inventory/suppliers, /inventory/guide.
// Who may open each one is decided in ONE place, ROUTE_ROLES (core/config/route-roles.ts); the tabs and the sidebar read the same entries.
export const INVENTORY_ROUTES: Routes = [
  { path: 'inventory', loadComponent: () => import('./pages/inventory-dashboard/inventory-dashboard.component').then((m) => m.InventoryDashboardComponent) },
  { path: 'inventory/items', loadComponent: () => import('./pages/items/items.component').then((m) => m.InventoryItemsComponent) },
  { path: 'inventory/add-stock', loadComponent: () => import('./pages/add-stock/add-stock.component').then((m) => m.InventoryAddStockComponent) },
  { path: 'inventory/adjustments', loadComponent: () => import('./pages/adjustments/adjustments.component').then((m) => m.InventoryAdjustmentsComponent) },
  { path: 'inventory/issues', loadComponent: () => import('./pages/issues/issues.component').then((m) => m.InventoryIssuesComponent) },
  { path: 'inventory/suppliers', loadComponent: () => import('./pages/suppliers/suppliers.component').then((m) => m.InventorySuppliersComponent) },
  { path: 'inventory/guide', loadComponent: () => import('./pages/guide/guide.component').then((m) => m.InventoryGuideComponent) },
];
