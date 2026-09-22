import { Routes } from '@angular/router';

export const SCHL_DATA_ROUTES: Routes = [

    {
        path: 'schools',
        loadComponent: () =>
            import('./pages/school-list/school-list.component').then(
                (m) => m.SchoolListComponent),
        title: 'Schools',
    },
    {
        path: 'add-schools',
        loadComponent: () =>
            import('./pages/school-registration/add-school.component').then(
                (m) => m.AddSchoolComponent),
        title: 'Create Organization — smss-web',
    },
];