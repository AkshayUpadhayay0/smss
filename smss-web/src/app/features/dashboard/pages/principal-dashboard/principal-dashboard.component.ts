import { PendingPromotionTileComponent } from '../../../promotion/components/pending-promotion-tile/pending-promotion-tile.component';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { historyYearOptions } from '../../../../core/utils/history-options';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectOption } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { describeWhen } from '../../../calendar/utils/calendar-dates';
import { audienceLabel, publishedLabel } from '../../../notices/utils/notice-format';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { DashboardYearBarComponent } from '../../components/dashboard-year-bar/dashboard-year-bar.component';
import { PrincipalDashboard } from '../../models/principal-dashboard.model';
import { PrincipalDashboardService } from '../../services/principal-dashboard.service';
import { attendanceTrendOptions } from '../../utils/role-dashboard-charts';
import { buildPrincipalAlerts, buildPrincipalStats } from '../../utils/principal-dashboard-format';
import { readChartColors } from '../../utils/super-admin-charts';

/**
 * The Principal's /dashboard: head counts, today's attendance and the 14-day trend, the fee numbers (read-only, as in the Fee Dashboard), what waits for a decision (leave,
 * admissions), the latest published results, upcoming events and latest notices. One request (GET /api/PrincipalDashboard/summary). A part the server could not load says
 * "Unavailable" rather than showing 0; each empty category says it is empty.
 */
@Component({
  selector: 'app-principal-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, ChartHostComponent, DashboardYearBarComponent, PendingPromotionTileComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../role-dashboard.scss',
  templateUrl: './principal-dashboard.component.html',
})
export class PrincipalDashboardComponent implements OnInit {
  private readonly service = inject(PrincipalDashboardService);
  private readonly yearService = inject(AcademicYearService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly colors = readChartColors();

  protected readonly breadcrumbs = [{ label: 'Dashboard' }];
  protected readonly audience = audienceLabel;
  protected readonly published = publishedLabel;
  protected readonly date = formatDate;
  protected readonly when = describeWhen;

  /** Every year of the school: the dashboard can LOOK at any of them. This only chooses what is shown, never the school's current year. */
  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly selectedYear = computed(() => String(this.data()?.academicYearId ?? ''));
  protected readonly data = signal<PrincipalDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly cards = computed(() => {
    const d = this.data();
    return d ? buildPrincipalStats(d) : [];
  });
  protected readonly alerts = computed(() => {
    const d = this.data();
    return d ? buildPrincipalAlerts(d) : [];
  });

  protected readonly attendanceChart = computed(() => attendanceTrendOptions(this.data()?.attendanceTrend ?? [], this.colors));
  protected readonly anyAttendance = computed(() => (this.data()?.attendanceTrend ?? []).some((p) => p.presentPercent != null));

  protected readonly attendanceDown = computed(() => this.data()?.partsUnavailable.includes('attendance') ?? false);
  protected readonly resultsDown = computed(() => this.data()?.partsUnavailable.includes('results') ?? false);
  protected readonly eventsDown = computed(() => this.data()?.partsUnavailable.includes('events') ?? false);
  protected readonly noticesDown = computed(() => this.data()?.partsUnavailable.includes('notices') ?? false);

  protected readonly subtitle = computed(() => {
    const year = this.data()?.academicYearName;
    return year ? `The school at a glance · academic year ${year}` : 'The school at a glance';
  });

  protected scopeLabel(scope: string, term: string | null): string {
    return scope === 'YEAR' ? 'Whole year' : (term ?? 'Term');
  }

  ngOnInit(): void {
    this.yearService.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (years) => this.yearOptions.set(historyYearOptions(years)),
      error: () => undefined,   // the dashboard still works on the current year without the selector
    });
    this.load();
  }

  protected load(academicYearId?: number): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .summary(academicYearId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (d) => this.data.set(d), error: () => this.loadFailed.set(true) });
  }
}
