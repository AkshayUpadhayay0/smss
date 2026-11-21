import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { WelcomeBannerComponent } from '../../components/welcome-banner/welcome-banner.component';
import { SchoolAdminDashboard } from '../../models/school-admin-dashboard.model';
import { SchoolAdminDashboardService } from '../../services/school-admin-dashboard.service';
import { attendanceView, monthLabel, employeeLines, incomeExpenseOptions, isEmptyDashboard, moneyCards, paidDonutOptions, studentsAndFeesCards } from '../../utils/school-admin-dashboard-format';
import { readChartColors } from '../../utils/super-admin-charts';

/**
 * The School Admin's /dashboard, in three sections: Students & Fees, Money, People. One request (GET /api/Dashboard/school-admin), read-only; the server
 * does all the counting. A school without an expenses record shows "Not set up yet" (never 0), and the vehicles card is hidden when the school has no vehicles.
 */
@Component({
  selector: 'app-school-admin-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, IconComponent, ChartHostComponent, WelcomeBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './school-admin-dashboard.component.scss',
  templateUrl: './school-admin-dashboard.component.html',
})
export class SchoolAdminDashboardComponent implements OnInit {
  private readonly service = inject(SchoolAdminDashboardService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly colors = readChartColors();

  protected readonly data = signal<SchoolAdminDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly feeCards = computed(() => (this.data() ? studentsAndFeesCards(this.data()!) : []));
  protected readonly moneyCards = computed(() => (this.data() ? moneyCards(this.data()!) : []));
  protected readonly attendance = computed(() => (this.data() ? attendanceView(this.data()!) : null));
  protected readonly employeeLines = computed(() => (this.data() ? employeeLines(this.data()!) : []));
  protected readonly empty = computed(() => (this.data() ? isEmptyDashboard(this.data()!) : false));
  protected readonly anyDues = computed(() => {
    const d = this.data();
    return !!d && d.studentsPaidThisMonth + d.studentsNotPaidThisMonth > 0;
  });
  protected readonly donut = computed(() => paidDonutOptions(this.data()!, this.colors));
  protected readonly incomeExpense = computed(() => incomeExpenseOptions(this.data()!, this.colors));
  protected readonly monthName = computed(() => (this.data() ? monthLabel(this.data()!.monthStart) : ''));
  protected readonly showVehicles = computed(() => (this.data()?.activeVehicles ?? 0) > 0);

  protected readonly welcomeTitle = computed(() => `Welcome Back, ${this.auth.user()?.roles?.[0] ?? 'School Admin'}!`);
  protected readonly welcomeSub = computed(() => `Here's what's happening at ${this.auth.user()?.schoolName || 'your school'}`);
  protected readonly yearBadge = computed(() => {
    const s = this.data()?.session;
    return s ? `Academic Year ${s.name}` : null;
  });

  protected readonly skeletons = [0, 1, 2, 3, 4];

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .load()
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (d) => this.data.set(d), error: () => this.loadFailed.set(true) });
  }
}
