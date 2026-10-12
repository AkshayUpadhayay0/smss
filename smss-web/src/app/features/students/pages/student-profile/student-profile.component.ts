import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { initialsOf, toUploadUrl } from '../../../../core/utils/upload-url';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { StudentPhotoDialogComponent } from '../../components/student-photo-dialog/student-photo-dialog.component';
import { formatDate } from '../../../admissions/models/admission.model';
import { GuardianRow, StudentPhoto, StudentProfile } from '../../models/student.model';
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
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, StudentPhotoDialogComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-profile.component.scss',
  templateUrl: './student-profile.component.html',
})
export class StudentProfileComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :studentId route param (withComponentInputBinding). */
  readonly studentId = input.required<string>();

  protected readonly student = signal<StudentProfile | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly guardianColumns = GUARDIAN_COLUMNS;
  protected readonly fmt = formatDate;

  /** The list's filter, carried in the URL so "Back to Students" returns to the same class and section. */
  private readonly filterParams = computed(() => {
    const q = this.route.snapshot.queryParamMap;
    const params: Record<string, string> = {};
    for (const k of ['academicYearId', 'classId', 'sectionId']) {
      const v = q.get(k);
      if (v) params[k] = v;
    }
    return params;
  });

  protected readonly breadcrumbs = computed(() => [
    { label: 'Students', link: '/students' },
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

  protected onPhotoSaved(photo: StudentPhoto): void {
    this.student.update((s) => (s ? { ...s, profilePhotoUrl: photo.profilePhotoUrl } : s));
    this.photoDialogOpen.set(false);
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    const id = Number(this.studentId());
    if (!Number.isFinite(id)) {
      this.loadFailed.set(true);
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.loadFailed.set(false);
    this.students
      .getProfile(id)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => this.student.set(s),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected back(): void {
    void this.router.navigate(['/students'], { queryParams: this.filterParams() });
  }
}
