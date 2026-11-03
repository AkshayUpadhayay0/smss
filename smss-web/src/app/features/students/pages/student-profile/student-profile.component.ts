import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { initialsOf, toUploadUrl } from '../../../../core/utils/upload-url';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { StudentFeesTabComponent } from '../../components/student-fees-tab/student-fees-tab.component';
import { StudentLoginDialogComponent } from '../../components/student-login-dialog/student-login-dialog.component';
import { StudentPhotoDialogComponent } from '../../components/student-photo-dialog/student-photo-dialog.component';
import { formatDate } from '../../../admissions/models/admission.model';
import { GuardianRow, StudentLoginCreated, StudentPhoto, StudentProfile } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

const GUARDIAN_COLUMNS: TableColumn<GuardianRow>[] = [
  { key: 'fullName', label: 'Name' },
  { key: 'relationship', label: 'Relationship' },
  { key: 'mobileNumber', label: 'Mobile' },
  { key: 'email', label: 'Email' },
  { key: 'primaryLabel', label: 'Primary', type: 'badge' },
  { key: 'emergencyLabel', label: 'Emergency Contact', type: 'badge' },
  { key: 'pickupLabel', label: 'Can Pickup', type: 'badge' },
];

const titleCase = (s: string) => (s ? s.charAt(0) + s.slice(1).toLowerCase() : s);

/** Read-only student profile (only the photo can be changed here; other student edits aren't built on the backend yet). */
@Component({
  selector: 'app-student-profile',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, StudentFeesTabComponent, StudentLoginDialogComponent, StudentPhotoDialogComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-profile.component.scss',
  templateUrl: './student-profile.component.html',
})
export class StudentProfileComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);

  /** Bound from the :studentId route param (withComponentInputBinding). */
  readonly studentId = input.required<string>();
  /**
   * Restricted (read-only) mode, set by the /my-students/:studentId route data (a Teacher looking at a student of their own class): loads from the narrow
   * my-class endpoint and shows the Profile only, with no photo change, no login, no Aadhaar and no Fees tab.
   */
  readonly restricted = input(false);

  protected readonly student = signal<StudentProfile | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly guardianColumns = GUARDIAN_COLUMNS;
  protected readonly fmt = formatDate;

  /** The list's filter, carried in the URL so "Back to Students" returns to the same class and section. */
  private readonly filterParams = computed(() => {
    const q = this.route.snapshot.queryParamMap;
    const params: Record<string, string> = {};
    for (const k of ['academicYearId', 'classId', 'sectionId', 'feeStatus']) {
      const v = q.get(k);
      if (v) params[k] = v;
    }
    return params;
  });

  protected readonly breadcrumbs = computed(() => [
    this.restricted() ? { label: 'My Students', link: '/my-students' } : { label: 'Students', link: '/students' },
    { label: this.student()?.fullName ?? 'Student' },
  ]);

  protected readonly guardianRows = computed<GuardianRow[]>(() =>
    (this.student()?.guardians ?? []).map((g) => ({
      guardianId: g.guardianId,
      fullName: g.fullName,
      relationship: titleCase(g.relationship),
      mobileNumber: g.mobileNumber,
      email: g.email ?? '—',
      primaryLabel: g.isPrimary ? 'Yes' : 'No',
      emergencyLabel: g.isEmergencyContact ? 'Yes' : 'No',
      pickupLabel: g.canPickupStudent ? 'Yes' : 'No',
    })),
  );

  protected readonly hasEnrollment = computed(() => !!this.student()?.enrollmentId);

  protected readonly photoUrl = computed(() => toUploadUrl(this.student()?.profilePhotoUrl));
  protected readonly initials = computed(() => initialsOf(this.student()?.fullName));
  protected readonly photoDialogOpen = signal(false);

  /** Profile (default) or Fees. The Fees tab is created only when first opened, so it loads on demand. */
  /**
   * The Profile tab has edit actions (photo, creating / resetting the student's login) and personal details: School Admin only.
   * An Accountant, who is here to collect and void payments, gets the student summary and the Fees tab only.
   */
  protected readonly isSchoolAdmin = computed(() => this.auth.currentUserRoles().some((r) => r.trim().toLowerCase() === ROLES.SCHOOL_ADMIN.toLowerCase()));
  /** The Profile tab is shown to a School Admin and, read-only, in restricted mode; everyone else (an Accountant) gets the Fees tab only. */
  protected readonly showProfile = computed(() => this.restricted() || this.isSchoolAdmin());
  /** Fees are never part of the restricted view. */
  protected readonly showFees = computed(() => !this.restricted());
  /**
   * Derived, not initialised once: the route binds `restricted` AFTER construction, so a field initialiser would still see false and open the Fees tab
   * (and call the fee endpoints) for a Teacher. The tab the user picked wins; otherwise Profile when it exists, else Fees. The Fees tab is created only when first
   * opened, so it loads on demand.
   */
  private readonly pickedTab = signal<'profile' | 'fees' | null>(null);
  private readonly feesVisited = signal(false);
  protected readonly tab = computed<'profile' | 'fees'>(() => {
    const picked = this.pickedTab();
    if (picked && (picked === 'profile' ? this.showProfile() : this.showFees())) return picked;
    return this.showProfile() ? 'profile' : 'fees';
  });
  protected readonly feesOpened = computed(() => this.showFees() && (this.feesVisited() || !this.showProfile()));

  protected selectTab(tab: 'profile' | 'fees'): void {
    this.pickedTab.set(tab);
    if (tab === 'fees') this.feesVisited.set(true);
  }

  protected readonly loginDialogOpen = signal(false);

  /** The user has seen the credentials: close the dialog and show the Login ID on the profile. */
  protected onLoginCreated(login: StudentLoginCreated): void {
    this.student.update((s) => (s ? { ...s, loginId: login.orgUserId } : s));
    this.loginDialogOpen.set(false);
  }

  protected onPhotoSaved(photo: StudentPhoto): void {
    this.student.update((s) => (s ? { ...s, profilePhotoUrl: photo.profilePhotoUrl } : s));
    this.photoDialogOpen.set(false);
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    // The route param IS the student id (a string like DPS001ST001): never convert it to a number
    const id = this.studentId().trim();
    if (!id) {
      this.loadFailed.set(true);
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.loadFailed.set(false);
    (this.restricted() ? this.students.getMyClassStudent(id) : this.students.getProfile(id))
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => this.student.set(s),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  /** Where "back" goes: the student list for a School Admin; the class report they came from for anyone else. */
  protected readonly backLabel = computed(() => (this.restricted() ? 'Back to My Students' : this.isSchoolAdmin() ? 'Back to Students' : 'Back to Class Report'));

  protected back(): void {
    if (this.restricted()) {
      void this.router.navigate(['/my-students']);
      return;
    }
    void this.router.navigate([this.isSchoolAdmin() ? '/students' : '/fee-reports/class'], { queryParams: this.filterParams() });
  }
}
