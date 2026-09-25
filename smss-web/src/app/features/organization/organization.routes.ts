import { Routes } from '@angular/router';
import { SchoolListComponent } from './pages/school-list/school-list.component';
import { AddSchoolComponent } from './pages/school-registration/add-school.component';


export const SCHL_DATA_ROUTES: Routes = [

    { path: 'schools', component: SchoolListComponent },
    { path: 'schools/add', component: AddSchoolComponent },
    { path: 'schools/:schoolId/edit', component: AddSchoolComponent }
    
];