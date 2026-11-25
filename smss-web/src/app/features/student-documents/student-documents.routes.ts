import { Routes } from '@angular/router';

// Mounted at the root: /student-documents (the pending list). School Admin, Principal and Admission Staff open it (ROUTE_ROLES).
export const STUDENT_DOCUMENTS_ROUTES: Routes = [
  {
    path: 'student-documents',
    loadComponent: () => import('./pages/pending-documents/pending-documents.component').then((m) => m.PendingDocumentsComponent),
  },
];
