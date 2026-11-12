import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { Breadcrumb, IconName } from '../../../../shared/components';
import { PlaceholderPageComponent } from '../../../placeholder/placeholder-page.component';
import { PrincipalDashboardComponent } from '../principal-dashboard/principal-dashboard.component';
import { SchoolDashboardComponent } from '../school-dashboard/school-dashboard.component';
import { SuperAdminDashboardComponent } from '../super-admin-dashboard/super-admin-dashboard.component';
import { StudentDashboardComponent } from '../student-dashboard/student-dashboard.component';
import { TeacherDashboardComponent } from '../teacher-dashboard/teacher-dashboard.component';

/**
 * /dashboard for every role. A Student, a Teacher, the Super Admin and a School Admin get their own dashboard; every other role still sees the "coming soon"
 * placeholder (fed from the same route data as before) until its own dashboard is built.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [PlaceholderPageComponent, PrincipalDashboardComponent, SchoolDashboardComponent, StudentDashboardComponent, SuperAdminDashboardComponent, TeacherDashboardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isStudent()) {
      <app-student-dashboard />
    } @else if (isSuperAdmin()) {
      <app-super-admin-dashboard />
    } @else if (isTeacher()) {
      <app-teacher-dashboard />
    } @else if (isSchoolAdmin()) {
      <app-school-dashboard />
    } @else if (isPrincipal()) {
      <app-principal-dashboard />
    } @else {
      <app-placeholder-page [title]="title()" [subtitle]="subtitle()" [icon]="icon()" [breadcrumbs]="breadcrumbs()" />
    }
  `,
})
export class DashboardPageComponent {
  private readonly auth = inject(AuthService);

  // route `data`, bound to inputs (withComponentInputBinding), passed straight on to the placeholder
  readonly title = input('Dashboard');
  readonly subtitle = input<string>();
  readonly icon = input<IconName>('layout-dashboard');
  readonly breadcrumbs = input<Breadcrumb[]>([]);

  protected readonly isStudent = computed(() => this.auth.currentUserRoles().includes(ROLES.STUDENT));
  // Student wins if an account somehow holds both, so the existing Student behaviour never changes
  protected readonly isSuperAdmin = computed(() => this.auth.currentUserRoles().includes(ROLES.SUPER_ADMIN));
  protected readonly isTeacher = computed(() => this.auth.currentUserRoles().includes(ROLES.TEACHER));
  // checked last, so an account that holds another dashboard's role as well keeps seeing that dashboard exactly as before
  protected readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().includes(ROLES.SCHOOL_ADMIN));
  // the role dashboards below come last for the same reason: an account that also holds one of the roles above keeps that dashboard
  protected readonly isPrincipal = computed(() => this.auth.currentUserRoles().includes(ROLES.PRINCIPAL));
}
