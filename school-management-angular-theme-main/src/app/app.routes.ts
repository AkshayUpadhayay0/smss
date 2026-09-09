import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },

  {
    path: 'auth',
    loadComponent: () => import('./layout/auth-layout/auth-layout').then(m => m.AuthLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'login' },
      { path: 'login', loadComponent: () => import('./auth/login/login').then(m => m.LoginComponent) },
      { path: 'register', loadComponent: () => import('./auth/register/register').then(m => m.RegisterComponent) },
      { path: 'forgot-password', loadComponent: () => import('./auth/forgot-password/forgot-password').then(m => m.ForgotPasswordComponent) },
      { path: 'reset-password', loadComponent: () => import('./auth/reset-password/reset-password').then(m => m.ResetPasswordComponent) },
      { path: 'otp', loadComponent: () => import('./auth/otp-verification/otp-verification').then(m => m.OtpVerificationComponent) },
    ],
  },

  {
    path: '',
    loadComponent: () => import('./layout/admin-layout/admin-layout').then(m => m.AdminLayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard').then(m => m.DashboardComponent), title: 'Dashboard' },

      { path: 'students', loadComponent: () => import('./features/students/student-list').then(m => m.StudentListComponent), title: 'Students' },
      { path: 'students/new', loadComponent: () => import('./features/students/student-form').then(m => m.StudentFormComponent), title: 'Add Student' },
      { path: 'students/:id/edit', loadComponent: () => import('./features/students/student-form').then(m => m.StudentFormComponent), title: 'Edit Student' },
      { path: 'students/:id', loadComponent: () => import('./features/students/student-profile').then(m => m.StudentProfileComponent), title: 'Student Profile' },

      { path: 'teachers', loadComponent: () => import('./features/teachers/teacher-list').then(m => m.TeacherListComponent), title: 'Teachers' },
      { path: 'teachers/:id', loadComponent: () => import('./features/teachers/teacher-profile').then(m => m.TeacherProfileComponent), title: 'Teacher Profile' },

      { path: 'classes', loadComponent: () => import('./features/classes/class-list').then(m => m.ClassListComponent), title: 'Classes & Sections' },
      { path: 'subjects', loadComponent: () => import('./features/subjects/subject-list').then(m => m.SubjectListComponent), title: 'Subjects' },
      { path: 'sessions', loadComponent: () => import('./features/sessions/session-list').then(m => m.SessionListComponent), title: 'Academic Sessions' },

      { path: 'attendance', loadComponent: () => import('./features/attendance/attendance-mark').then(m => m.AttendanceMarkComponent), title: 'Attendance' },
      { path: 'attendance/reports', loadComponent: () => import('./features/attendance/attendance-reports').then(m => m.AttendanceReportsComponent), title: 'Attendance Reports' },

      { path: 'timetable', loadComponent: () => import('./features/timetable/timetable-view').then(m => m.TimetableViewComponent), title: 'Timetable' },

      { path: 'homework', loadComponent: () => import('./features/homework/homework-list').then(m => m.HomeworkListComponent), title: 'Homework' },

      { path: 'examinations', loadComponent: () => import('./features/examinations/exam-list').then(m => m.ExamListComponent), title: 'Examinations' },
      { path: 'examinations/marks', loadComponent: () => import('./features/examinations/marks-entry').then(m => m.MarksEntryComponent), title: 'Marks Entry' },
      { path: 'results', loadComponent: () => import('./features/results/results-view').then(m => m.ResultsViewComponent), title: 'Results' },

      { path: 'admissions', loadComponent: () => import('./features/admissions/admission-list').then(m => m.AdmissionListComponent), title: 'Admissions' },

      { path: 'fees', loadComponent: () => import('./features/fees/fees-dashboard').then(m => m.FeesDashboardComponent), title: 'Fees' },

      { path: 'library', loadComponent: () => import('./features/library/library-dashboard').then(m => m.LibraryDashboardComponent), title: 'Library' },

      { path: 'transport', loadComponent: () => import('./features/transport/transport-dashboard').then(m => m.TransportDashboardComponent), title: 'Transport' },

      { path: 'hostel', loadComponent: () => import('./features/hostel/hostel-dashboard').then(m => m.HostelDashboardComponent), title: 'Hostel' },

      { path: 'calendar', loadComponent: () => import('./features/calendar/calendar-view').then(m => m.CalendarViewComponent), title: 'Calendar' },

      { path: 'notices', loadComponent: () => import('./features/notices/notice-list').then(m => m.NoticeListComponent), title: 'Notices' },
      { path: 'notifications', loadComponent: () => import('./features/notifications/notification-center').then(m => m.NotificationCenterComponent), title: 'Notifications' },
      { path: 'messages', loadComponent: () => import('./features/notifications/messages').then(m => m.MessagesComponent), title: 'Messages' },

      { path: 'reports', loadComponent: () => import('./features/reports/reports-hub').then(m => m.ReportsHubComponent), title: 'Reports' },

      { path: 'ui', loadComponent: () => import('./features/ui-showcase/ui-showcase').then(m => m.UiShowcaseComponent), title: 'UI Components' },

      { path: 'settings', loadComponent: () => import('./features/settings/settings-page').then(m => m.SettingsPageComponent), title: 'Settings' },
      { path: 'profile', loadComponent: () => import('./features/profile/profile-page').then(m => m.ProfilePageComponent), title: 'My Profile' },

      { path: 'portal', loadComponent: () => import('./features/parent-portal/parent-portal').then(m => m.ParentPortalComponent), title: 'Parent Portal' },

      { path: '403', loadComponent: () => import('./features/errors/forbidden').then(m => m.ForbiddenComponent) },
      { path: '500', loadComponent: () => import('./features/errors/server-error').then(m => m.ServerErrorComponent) },
      { path: 'maintenance', loadComponent: () => import('./features/errors/maintenance').then(m => m.MaintenanceComponent) },
    ],
  },

  { path: '**', loadComponent: () => import('./features/errors/not-found').then(m => m.NotFoundComponent) },
];
