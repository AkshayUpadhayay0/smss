import { Routes } from '@angular/router';

// Mounted at the root: /examinations/... (Examination / Report Card, stage 1: setup).
// The literal "new" and "blank" routes come BEFORE ":schemeId" so they are never read as a scheme id.
export const EXAMINATIONS_ROUTES: Routes = [
  { path: 'examinations/grade-scales', loadComponent: () => import('./pages/grade-scales/grade-scales.component').then((m) => m.GradeScalesComponent) },
  { path: 'examinations/schemes', loadComponent: () => import('./pages/report-schemes/report-schemes.component').then((m) => m.ReportSchemesComponent) },
  { path: 'examinations/schemes/new', loadComponent: () => import('./pages/scheme-from-preset/scheme-from-preset.component').then((m) => m.SchemeFromPresetComponent) },
  { path: 'examinations/schemes/blank', loadComponent: () => import('./pages/scheme-editor/scheme-editor.component').then((m) => m.SchemeEditorComponent) },
  { path: 'examinations/schemes/:schemeId', loadComponent: () => import('./pages/scheme-editor/scheme-editor.component').then((m) => m.SchemeEditorComponent) },
  { path: 'examinations/activity-areas', loadComponent: () => import('./pages/activity-areas/activity-areas.component').then((m) => m.ActivityAreasComponent) },
  { path: 'examinations/marks', loadComponent: () => import('./pages/marks-entry/marks-entry.component').then((m) => m.MarksEntryComponent) },
  { path: 'examinations/class-report', loadComponent: () => import('./pages/class-report/class-report.component').then((m) => m.ClassReportComponent) },
  { path: 'examinations/results', loadComponent: () => import('./pages/results-publish/results-publish.component').then((m) => m.ResultsPublishComponent) },
  { path: 'my-report-cards', loadComponent: () => import('./pages/my-report-cards/my-report-cards.component').then((m) => m.MyReportCardsComponent) },
  { path: 'examinations/schedule', loadComponent: () => import('./pages/exam-schedule/exam-schedule.component').then((m) => m.ExamScheduleComponent) },
];
