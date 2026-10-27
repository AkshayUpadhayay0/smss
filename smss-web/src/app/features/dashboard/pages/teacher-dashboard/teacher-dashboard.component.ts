import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { initialsOf, toUploadUrl } from '../../../../core/utils/upload-url';
import { CardComponent, IconComponent, IconName, PageHeaderComponent } from '../../../../shared/components';
import { TeacherDashboard } from '../../models/teacher-dashboard.model';
import { TeacherDashboardService } from '../../services/teacher-dashboard.service';
import { TeacherStat, buildTeacherStats, sectionLabel } from '../../utils/teacher-dashboard-format';

const STAT_ICONS: Record<TeacherStat['key'], IconName> = {
  students: 'graduation-cap',
  fees: 'file-text',
  present: 'circle-check',
  absent: 'x',
};

/**
 * The Teacher dashboard: read-only, one request (GET /api/TeacherDashboard). Everything shown is the signed-in teacher's own data.
 * No edit-profile; the fee card is a head count only (no amounts, no student list).
 */
@Component({
  selector: 'app-teacher-dashboard',
  imports: [CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './teacher-dashboard.component.scss',
  templateUrl: './teacher-dashboard.component.html',
})
export class TeacherDashboardComponent implements OnInit {
  private readonly service = inject(TeacherDashboardService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Dashboard' }];
  protected readonly initials = initialsOf;
  protected readonly photoUrl = toUploadUrl;
  protected readonly sectionLabel = sectionLabel;
  protected readonly icons = STAT_ICONS;

  protected readonly data = signal<TeacherDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly subtitle = computed(() => {
    const name = this.data()?.profile.fullName.split(' ')[0];
    return name ? `Welcome back, ${name}. Here is your classroom at a glance.` : 'Your classroom at a glance.';
  });

  protected readonly stats = computed(() => {
    const d = this.data();
    return d ? buildTeacherStats(d) : [];
  });

  protected readonly classTeacherDown = computed(() => this.data()?.partsUnavailable.includes('classTeacherOf') ?? false);
  protected readonly subjectsDown = computed(() => this.data()?.partsUnavailable.includes('subjectsTaught') ?? false);

  ngOnInit(): void {
    this.service
      .get()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (d) => this.data.set(d), error: () => this.loadFailed.set(true) });
  }
}
