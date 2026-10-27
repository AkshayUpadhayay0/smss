import { Routes } from '@angular/router';

// Mounted at the root: /notices (School Admin, Principal: manage) and /notices/my (every school role: read).
export const NOTICES_ROUTES: Routes = [
  { path: 'notices/my', loadComponent: () => import('./pages/my-notices/my-notices.component').then((m) => m.MyNoticesComponent) },
  { path: 'notices', loadComponent: () => import('./pages/notice-list/notice-list.component').then((m) => m.NoticeListComponent) },
];
