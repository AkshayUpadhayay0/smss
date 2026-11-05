import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { toUploadUrl } from '../../../../core/utils/upload-url';
import { CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { DashboardAttendance } from '../../../dashboard/models/student-dashboard.model';
import { StudentDashboardService } from '../../../dashboard/services/student-dashboard.service';
import { StudentMyLeaveComponent } from '../../../leave/components/student-my-leave/student-my-leave.component';
import { StudentMyProfile } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

type Tab = 'details' | 'attendance' | 'leave';

function initialsOf(name?: string | null): string {
  return (name ?? '').split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]!.toUpperCase()).join('') || '?';
}

/**
 * "My Profile" for a Student login: their OWN record (GET /api/Student/me), read-only: no edit, no photo change. The Attendance tab shows the
 * year summary the Student dashboard already reads (no extra endpoint). Fees, exams, library, bank and previous-school sections do not exist yet.
 */
@Component({
  selector: 'app-my-student-profile',
  imports: [CardComponent, PageHeaderComponent, StudentMyLeaveComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './my-student-profile.component.scss',
  templateUrl: './my-student-profile.component.html',
})
export class MyStudentProfileComponent implements OnInit {
  private readonly students = inject(StudentService);
  private readonly dashboard = inject(StudentDashboardService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly profile = signal<StudentMyProfile | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly tab = signal<Tab>('details');

  protected readonly attendance = signal<DashboardAttendance | null>(null);
  protected readonly attendanceLoading = signal(false);
  protected readonly attendanceFailed = signal(false);
  private attendanceLoaded = false;

  protected readonly fmt = formatDate;
  protected readonly initialsOf = initialsOf;
  protected readonly breadcrumbs = [{ label: 'My Profile' }];
  protected readonly photoUrl = computed(() => toUploadUrl(this.profile()?.profilePhotoUrl));
  protected readonly initials = computed(() => initialsOf(this.profile()?.fullName));
  /** The Leave tab loads its requests the first time it is opened. */
  protected readonly leaveOpened = signal(false);
  protected readonly isActive = computed(() => this.profile()?.statusName === 'Active');

  ngOnInit(): void {
    this.students
      .me()
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (p) => this.profile.set(p), error: () => this.loadFailed.set(true) }); // already toasted by the interceptor
  }

  protected selectTab(tab: Tab): void {
    this.tab.set(tab);
    if (tab === 'leave') this.leaveOpened.set(true);
    if (tab === 'attendance' && !this.attendanceLoaded) this.loadAttendance();
  }

  protected loadAttendance(): void {
    this.attendanceLoaded = true;
    this.attendanceLoading.set(true);
    this.attendanceFailed.set(false);
    this.dashboard
      .get()
      .pipe(finalize(() => this.attendanceLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (d) => this.attendance.set(d.attendance ?? null), error: () => this.attendanceFailed.set(true) });
  }
}
