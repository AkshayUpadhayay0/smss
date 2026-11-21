import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { Breadcrumb, IconName } from '../../../../shared/components';
import { PlaceholderPageComponent } from '../../../placeholder/placeholder-page.component';
import { AccountantDashboardComponent } from '../accountant-dashboard/accountant-dashboard.component';
import { AdmissionDashboardComponent } from '../admission-dashboard/admission-dashboard.component';
import { PrincipalDashboardComponent } from '../principal-dashboard/principal-dashboard.component';
import { SchoolAdminDashboardComponent } from '../school-admin-dashboard/school-admin-dashboard.component';
import { SuperAdminDashboardComponent } from '../super-admin-dashboard/super-admin-dashboard.component';
import { StudentDashboardComponent } from '../student-dashboard/student-dashboard.component';
import { TeacherDashboardComponent } from '../teacher-dashboard/teacher-dashboard.component';

/**
 * /dashboard for every role. A Student, a Teacher, the Super Admin, a School Admin, a Principal, an Accountant and Admission Staff get their own dashboard; every other role
 * (a Guardian) still sees the "coming soon" placeholder (fed from the same route data as before) until its own dashboard is built.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [AccountantDashboardComponent, AdmissionDashboardComponent, PlaceholderPageComponent, PrincipalDashboardComponent, SchoolAdminDashboardComponent, StudentDashboardComponent, SuperAdminDashboardComponent, TeacherDashboardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isStudent()) {
      <app-student-dashboard />
    } @else if (isSuperAdmin()) {
      <app-super-admin-dashboard />
    } @else if (isTeacher()) {
      <app-teacher-dashboard />
    } @else if (isSchoolAdmin()) {
      <app-school-admin-dashboard />
    } @else if (isPrincipal()) {
      <app-principal-dashboard />
    } @else if (isAccountant()) {
      <app-accountant-dashboard />
    } @else if (isAdmissionStaff()) {
      <app-admission-dashboard />
    } @else if (isStoreKeeperOnly()) {
      <!-- the Store Keeper's landing is /inventory (see the constructor); this is only seen for the instant before the redirect -->
      <p class="redirecting" role="status">Opening Inventory…</p>
    } @else {
      <app-placeholder-page [title]="title()" [subtitle]="subtitle()" [icon]="icon()" [breadcrumbs]="breadcrumbs()" />
    }
  `,
})
export class DashboardPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

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
  protected readonly isAccountant = computed(() => this.auth.currentUserRoles().includes(ROLES.ACCOUNTANT));
  protected readonly isAdmissionStaff = computed(() => this.auth.currentUserRoles().includes(ROLES.ADMISSION_STAFF));
  // The Store Keeper lands on Inventory. Only an account whose ONLY dashboard role is Store Keeper is sent there: one that also holds any role above keeps that
  // role's dashboard exactly as before, so no other role's landing changes.
  protected readonly isStoreKeeperOnly = computed(
    () =>
      this.auth.currentUserRoles().includes(ROLES.STORE_KEEPER) &&
      !(this.isStudent() || this.isSuperAdmin() || this.isTeacher() || this.isSchoolAdmin() || this.isPrincipal() || this.isAccountant() || this.isAdmissionStaff()),
  );

  constructor() {
    effect(() => {
      if (this.isStoreKeeperOnly()) void this.router.navigateByUrl('/inventory', { replaceUrl: true });
    });
  }
}
