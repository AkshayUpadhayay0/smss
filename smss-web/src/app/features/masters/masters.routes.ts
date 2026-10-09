import { Route, Routes } from '@angular/router';
import { BLOOD_GROUP_CONFIG } from './configs/blood-group.config';
import { BOARD_TYPE_CONFIG } from './configs/board-type.config';
import { DOCUMENT_TYPE_CONFIG } from './configs/document-type.config';
import { GENDER_CONFIG } from './configs/gender.config';
import { RELIGION_CATEGORY_CONFIG } from './configs/religion-category.config';
import { ROLE_CONFIG } from './configs/role.config';
import { SCHOOL_LEVEL_CONFIG } from './configs/school-level.config';
import { SCHOOL_TYPE_CONFIG } from './configs/school-type.config';
import { STATUS_CONFIG } from './configs/status.config';
import { STUDENT_CATEGORY_CONFIG } from './configs/student-category.config';
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
  master('religion-category', RELIGION_CATEGORY_CONFIG),
  master('blood-group', BLOOD_GROUP_CONFIG),
  master('gender', GENDER_CONFIG),
  master('document-type', DOCUMENT_TYPE_CONFIG),
  master('student-category', STUDENT_CATEGORY_CONFIG),
];
