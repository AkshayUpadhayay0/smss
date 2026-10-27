import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { toUploadUrl, initialsOf } from '../../../../core/utils/upload-url';
import { CardComponent, IconComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { describeWhen } from '../../../calendar/utils/calendar-dates';
import { day } from '../../../homework/utils/homework-format';
import { publishedLabel, snippet } from '../../../notices/utils/notice-format';
import { StudentDashboard } from '../../models/student-dashboard.model';
import { StudentDashboardService } from '../../services/student-dashboard.service';
import {
  CLASS_STATUS_LABELS,
  CLASS_STATUS_TONES,
  DONUT_CIRCUMFERENCE,
  DONUT_RADIUS,
  dateRange,
  donutArcs,
  leaveTone,
  marksText,
  timeRange,
} from '../../utils/dashboard-format';

interface ResultRow extends Record<string, unknown> {
  id: number;
  title: string;
  subjectName: string;
  typeName: string;
  date: string;
  marks: string;
}

const RESULT_COLUMNS: TableColumn<ResultRow>[] = [
  { key: 'id', label: 'ID', width: '64px' },
  { key: 'title', label: 'Test Name' },
  { key: 'subjectName', label: 'Subject' },
  { key: 'typeName', label: 'Type' },
  { key: 'date', label: 'Date' },
  { key: 'marks', label: 'Marks' },
];

/**
 * The Student dashboard: read-only, one request (GET /api/StudentDashboard). Everything shown is the signed-in student's own data.
 * No edit-profile, no grade / CGPA and no half-day / late attendance: none of those exist yet.
 */
@Component({
  selector: 'app-student-dashboard',
  imports: [CardComponent, IconComponent, PageHeaderComponent, RouterLink, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-dashboard.component.scss',
  templateUrl: './student-dashboard.component.html',
})
export class StudentDashboardComponent implements OnInit {
  private readonly service = inject(StudentDashboardService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Dashboard' }];
  protected readonly resultColumns = RESULT_COLUMNS;
  protected readonly classLabels = CLASS_STATUS_LABELS;
  protected readonly classTones = CLASS_STATUS_TONES;
  protected readonly circumference = DONUT_CIRCUMFERENCE;
  protected readonly radius = DONUT_RADIUS;
  protected readonly time = timeRange;
  protected readonly range = dateRange;
  protected readonly tone = leaveTone;
  protected readonly published = publishedLabel;
  protected readonly preview = snippet;
  protected readonly when = describeWhen;
  protected readonly initials = initialsOf;
  protected readonly photoUrl = toUploadUrl;
  protected readonly day = day;

  protected readonly data = signal<StudentDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly subtitle = computed(() => {
    const name = this.data()?.profile.fullName.split(' ')[0];
    return name ? `Welcome back, ${name}. Here is your school day at a glance.` : 'Your school day at a glance.';
  });

  protected readonly classLine = computed(() => {
    const p = this.data()?.profile;
    if (!p?.className) return 'Not enrolled in the current academic year';
    return p.sectionName ? `${p.className} – ${p.sectionName}` : p.className;
  });

  protected readonly attendance = computed(() => this.data()?.attendance ?? null);
  protected readonly arcs = computed(() => donutArcs(this.attendance()?.presentCount ?? 0, this.attendance()?.absentCount ?? 0));
  protected readonly percentText = computed(() => {
    const p = this.attendance()?.presentPercentage;
    return p == null ? '–' : `${p}%`;
  });

  protected readonly resultRows = computed<ResultRow[]>(() =>
    (this.data()?.recentResults ?? []).map((r) => ({
      id: r.assessmentId,
      title: r.title,
      subjectName: r.subjectName,
      typeName: r.assessmentTypeName,
      date: day(r.testDate),
      marks: marksText(r.obtainedMarks, r.maxMarks, r.isAbsent),
    })),
  );

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
