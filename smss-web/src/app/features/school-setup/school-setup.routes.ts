import { Route, Routes } from '@angular/router';
import {
  ADMISSION_TYPE_CONFIG,
  EMPLOYEE_DEPARTMENT_CONFIG,
  EMPLOYEE_DESIGNATION_CONFIG,
  SUBJECT_CONFIG,
} from './configs/school-lookups.config';
import { SchoolLookupConfig } from './models/school-lookup.model';

const lookupList = () => import('./pages/school-lookup-list/school-lookup-list.component').then((m) => m.SchoolLookupListComponent);

/** Each simple school-owned master is a config handed to the one generic list (bound to its `config` input). */
const lookup = (path: string, config: SchoolLookupConfig): Route => ({ path: `school-setup/${path}`, loadComponent: lookupList, data: { config } });

// Mounted at the root: /school-setup/...  School-owned records, as opposed to the global /masters lookups.
export const SCHOOL_SETUP_ROUTES: Routes = [
  { path: 'school-setup', pathMatch: 'full', redirectTo: 'school-setup/academic-year' },
  {
    path: 'school-setup/academic-year',
    loadComponent: () => import('./pages/academic-year-list/academic-year-list.component').then((m) => m.AcademicYearListComponent),
  },
  {
    path: 'school-setup/classes',
    loadComponent: () => import('./pages/class-list/class-list.component').then((m) => m.ClassListComponent),
  },
  {
    path: 'school-setup/sections',
    loadComponent: () => import('./pages/section-list/section-list.component').then((m) => m.SectionListComponent),
  },
  {
    path: 'school-setup/class-subjects',
    loadComponent: () => import('./pages/class-subject-mapping/class-subject-mapping.component').then((m) => m.ClassSubjectMappingComponent),
  },
  lookup('subjects', SUBJECT_CONFIG),
  lookup('employee-designations', EMPLOYEE_DESIGNATION_CONFIG),
  lookup('employee-departments', EMPLOYEE_DEPARTMENT_CONFIG),
  lookup('admission-types', ADMISSION_TYPE_CONFIG),
];
