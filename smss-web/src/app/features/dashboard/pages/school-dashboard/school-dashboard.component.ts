import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { AuthService } from '../../../../core/services/auth.service';
import { STATUS_META, AdmissionStatus, formatDate } from '../../../admissions/models/admission.model';
import { describeWhen } from '../../../calendar/utils/calendar-dates';
import { audienceLabel, publishedLabel } from '../../../notices/utils/notice-format';
import { SchoolDashboard } from '../../models/school-dashboard.model';
import { WelcomeBannerComponent } from '../../components/welcome-banner/welcome-banner.component';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { SchoolDashboardService } from '../../services/school-dashboard.service';
import { attendanceTrendOptions, feeCollectionOptions } from '../../utils/school-dashboard-charts';
import { buildSchoolAlerts, buildSchoolStats } from '../../utils/school-dashboard-format';
import { readChartColors } from '../../utils/super-admin-charts';

/**
 * The School Admin's /dashboard: six headline numbers, the attendance and fee-collection trends, alerts & reminders, and the next events, latest notices and
 * latest admission applications. One request (GET /api/SchoolDashboard/summary), read-only; every number comes from the module it belongs to, and each card
 * links to that module's screen. A part the server could not load says "Unavailable" rather than showing 0.
 */
@Component({
  selector: 'app-school-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, IconComponent, ChartHostComponent, WelcomeBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './school-dashboard.component.scss',
  templateUrl: './school-dashboard.component.html',
})
export class SchoolDashboardComponent implements OnInit {
  private readonly service = inject(SchoolDashboardService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly colors = readChartColors();

  protected readonly audience = audienceLabel;
  protected readonly published = publishedLabel;
  protected readonly date = formatDate;
  protected readonly when = describeWhen;

  protected readonly data = signal<SchoolDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly cards = computed(() => {
    const d = this.data();
    return d ? buildSchoolStats(d) : [];
  });
  protected readonly alerts = computed(() => {
    const d = this.data();
    return d ? buildSchoolAlerts(d) : [];
  });

  protected readonly attendanceChart = computed(() => attendanceTrendOptions(this.data()?.attendanceTrend ?? [], this.colors));
  protected readonly feeChart = computed(() => feeCollectionOptions(this.data()?.feeCollectionTrend ?? [], this.colors));
  // the 14 days always come back; a chart with no day marked at all would be an empty frame, so say so instead
  protected readonly anyAttendance = computed(() => (this.data()?.attendanceTrend ?? []).some((p) => p.presentPercent != null));
  protected readonly anyFees = computed(() => (this.data()?.feeCollectionTrend ?? []).some((m) => m.amount > 0));

  protected readonly attendanceDown = computed(() => this.data()?.partsUnavailable.includes('attendance') ?? false);
  protected readonly feesDown = computed(() => this.data()?.partsUnavailable.includes('fees') ?? false);
  protected readonly eventsDown = computed(() => this.data()?.partsUnavailable.includes('events') ?? false);
  protected readonly noticesDown = computed(() => this.data()?.partsUnavailable.includes('notices') ?? false);
  protected readonly admissionsDown = computed(() => this.data()?.partsUnavailable.includes('admissions') ?? false);

  protected readonly welcomeTitle = computed(() => `Welcome Back, ${this.roleLabel()}!`);
  protected readonly welcomeSub = computed(() => `Here's what's happening at ${this.schoolName()}`);
  protected readonly yearBadge = computed(() => {
    const y = this.data()?.academicYearName;
    return y ? `Academic Year: ${y}` : null;
  });
  protected readonly roleLabel = computed(() => this.auth.user()?.roles?.[0] ?? 'School Admin');
  protected readonly schoolName = computed(() => this.auth.user()?.schoolName || 'your school');
  /** Mean of the days that were actually marked (a day nothing was marked is not a 0%); null when no day was marked. */
  protected readonly avgAttendance = computed(() => {
    const v = (this.data()?.attendanceTrend ?? []).map((p) => p.presentPercent).filter((x): x is number => x != null);
    return v.length ? Math.round((v.reduce((a, b) => a + b, 0) / v.length) * 10) / 10 : null;
  });

  protected statusLabel(status: string): string {
    return STATUS_META[status as AdmissionStatus]?.label ?? status;
  }
  protected statusTone(status: string): string {
    return STATUS_META[status as AdmissionStatus]?.tone ?? 'gray';
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .summary()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (d) => this.data.set(d), error: () => this.loadFailed.set(true) });
  }
}
