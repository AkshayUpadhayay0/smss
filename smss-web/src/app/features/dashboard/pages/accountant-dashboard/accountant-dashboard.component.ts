import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { historyYearOptions } from '../../../../core/utils/history-options';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { publishedLabel } from '../../../notices/utils/notice-format';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { DashboardYearBarComponent } from '../../components/dashboard-year-bar/dashboard-year-bar.component';
import { AccountantDashboard } from '../../models/accountant-dashboard.model';
import { AccountantDashboardService } from '../../services/accountant-dashboard.service';
import { buildAccountantStats } from '../../utils/accountant-dashboard-format';
import { feeCollectionOptions } from '../../utils/role-dashboard-charts';
import { readChartColors } from '../../utils/super-admin-charts';

interface PaymentRow {
  receiptNumber: string;
  student: string;
  amount: string;
  paymentMode: string;
  paidOn: string;
}

/**
 * The Accountant's /dashboard: collected today and this month, outstanding, overdue and upcoming dues, the 6-month collection chart and the last 10 payments, with a link to
 * the class-wise report. One request (GET /api/AccountantDashboard/summary), read-only. A part the server could not load says "Unavailable" rather than showing 0.
 */
@Component({
  selector: 'app-accountant-dashboard',
  imports: [RouterLink, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, TableComponent, ChartHostComponent, DashboardYearBarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../role-dashboard.scss',
  templateUrl: './accountant-dashboard.component.html',
})
export class AccountantDashboardComponent implements OnInit {
  private readonly service = inject(AccountantDashboardService);
  private readonly yearService = inject(AcademicYearService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly colors = readChartColors();

  protected readonly breadcrumbs = [{ label: 'Dashboard' }];
  protected readonly columns: TableColumn<PaymentRow>[] = [
    { key: 'receiptNumber', label: 'Receipt' },
    { key: 'student', label: 'Student' },
    { key: 'amount', label: 'Amount' },
    { key: 'paymentMode', label: 'Mode' },
    { key: 'paidOn', label: 'Paid on' },
  ];

  /** Every year of the school: the dashboard can LOOK at any of them (the dues cards follow it). This never changes the school's current year. */
  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly selectedYear = computed(() => String(this.data()?.academicYearId ?? ''));
  protected readonly data = signal<AccountantDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly cards = computed(() => {
    const d = this.data();
    return d ? buildAccountantStats(d) : [];
  });
  protected readonly feeChart = computed(() => feeCollectionOptions(this.data()?.collectionTrend ?? [], this.colors));
  protected readonly anyFees = computed(() => (this.data()?.collectionTrend ?? []).some((m) => m.amount > 0));
  protected readonly feesDown = computed(() => this.data()?.partsUnavailable.includes('fees') ?? false);
  protected readonly paymentsDown = computed(() => this.data()?.partsUnavailable.includes('payments') ?? false);
  protected readonly rows = computed<PaymentRow[]>(() =>
    (this.data()?.recentPayments ?? []).map((p) => ({
      receiptNumber: p.receiptNumber,
      student: `${p.studentName} (${p.studentId})`,
      amount: formatInr(p.amount),
      paymentMode: p.paymentMode,
      paidOn: publishedLabel(p.paymentDate),
    })),
  );

  protected readonly subtitle = computed(() => {
    const year = this.data()?.academicYearName;
    return year ? `Fee collection at a glance · dues for academic year ${year}` : 'Fee collection at a glance';
  });

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
