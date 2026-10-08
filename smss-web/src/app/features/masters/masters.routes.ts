import { Route, Routes } from '@angular/router';
import { BOARD_TYPE_CONFIG } from './configs/board-type.config';
import { ROLE_CONFIG } from './configs/role.config';
import { SCHOOL_LEVEL_CONFIG } from './configs/school-level.config';
import { SCHOOL_TYPE_CONFIG } from './configs/school-type.config';
import { STATUS_CONFIG } from './configs/status.config';
import { MasterConfig } from './models/master.model';

const masterList = () => import('./pages/master-list/master-list.component').then((m) => m.MasterListComponent);

/** Each master is a config handed to the one generic list component (bound to its `config` input). */
const master = (path: string, config: MasterConfig): Route => ({ path: `masters/${path}`, loadComponent: masterList, data: { config } });

export const MASTERS_ROUTES: Routes = [
  { path: 'masters', pathMatch: 'full', redirectTo: 'masters/board-type' },
  master('board-type', BOARD_TYPE_CONFIG),
  master('school-type', SCHOOL_TYPE_CONFIG),
  master('school-level', SCHOOL_LEVEL_CONFIG),
  master('status', STATUS_CONFIG),
  master('role', ROLE_CONFIG),
];
