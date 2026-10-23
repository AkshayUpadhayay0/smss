import { Routes } from '@angular/router';

// Mounted at the root: /homework (Teacher, School Admin) and /homework/my (Student).
export const HOMEWORK_ROUTES: Routes = [
  { path: 'homework/my', loadComponent: () => import('./pages/my-homework/my-homework.component').then((m) => m.MyHomeworkComponent) },
  { path: 'homework', loadComponent: () => import('./pages/homework-list/homework-list.component').then((m) => m.HomeworkListComponent) },
];
