import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subscription, debounceTime, finalize, merge } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { DashboardStat, SuperAdminSummary } from '../../../subscriptions/models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../../subscriptions/services/super-admin-dashboard.service';
import { ChartHostComponent } from '../../components/chart/chart.component';
import {
  STATUS_SLICES,
  Trend,
  collectionSegments,
  readChartColors,
  registrationOptions,
  revenueExpenseOptions,
  signedInr,
  statusDonutOptions,
  trendOf,
} from '../../utils/super-admin-charts';

interface SchoolRow {
  name: string;
  schoolId: string;
  boardType: string;
  registered: string;
  status: string;
  [key: string]: unknown;
}

interface TransactionRow {
  reference: string;
  school: string;
  date: string;
  amount: string;
  method: string;
  status: string;
  [key: string]: unknown;
}

interface StatCard {
  id: string;
  label: string;
  value: string;
  /** The line under the value (what the number is a share of, or what it includes). */
  note: string;
  trend: Trend | null;
  /** Hover text for the trend: what it was compared with. */
  trendTitle: string;
  link: string;
  query?: Record<string, string>;
  tone: 'primary' | 'success' | 'info' | 'warning' | 'danger';
}

/**
 * The Super Admin's /dashboard: six headline numbers with their trend, three charts (revenue vs expenses, school registrations, subscription status),
 * the payment collection bar, quick actions, alerts, renewals and pricing, and the latest registrations and transactions. The date range moves the
 * six cards, the collection bar and the transactions; the two monthly charts always show the last twelve months. The schools and invoices tables live
 * on /subscriptions and /invoices; this page links there (pre-filtered where a filter says the same thing).
 */
@Component({
  selector: 'app-super-admin-dashboard',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, TableComponent, ChartHostComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './super-admin-dashboard.component.scss',
  templateUrl: './super-admin-dashboard.component.html',
})
export class SuperAdminDashboardComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly inr = formatInr;
  protected readonly date = formatDate;
  protected readonly statusSlices = STATUS_SLICES;
  private readonly colors = readChartColors();

  protected readonly summary = signal<SuperAdminSummary | null>(null);
  protected readonly summaryFailed = signal(false);
  protected readonly loading = signal(true);
  protected readonly invalidRange = signal(false);
  private request?: Subscription;

  protected readonly fromControl = new FormControl('', { nonNullable: true });
  protected readonly toControl = new FormControl('', { nonNullable: true });

  protected readonly cards = computed<StatCard[]>(() => {
    const s = this.summary();
    if (!s) return [];
    const st = s.stats;
    const vs = (stat: DashboardStat, fmt: (n: number) => string) =>
      stat.previousValue == null ? '' : `${fmt(stat.previousValue)} in ${this.date(s.previousFrom)} to ${this.date(s.previousTo)}`;
    const n = (x: number) => String(x);
    return [
      {
        id: 'schools', label: 'Total registered schools', value: n(st.totalSchools.value), tone: 'primary', link: '/schools',
        note: `${s.activeSchools} active, ${s.totalSchools - s.activeSchools} inactive`, trend: trendOf(st.totalSchools.trendPercent), trendTitle: vs(st.totalSchools, n),
      },
      {
        id: 'active', label: 'Active subscriptions', value: n(st.activeSubscriptions.value), tone: 'success', link: '/subscriptions', query: { status: 'ACTIVE' },
        note: `${st.activeSubscriptionsPercentOfTotal}% of all schools`, trend: null, trendTitle: '',
      },
      {
        id: 'collected', label: 'Payments collected', value: formatInr(st.paymentsCollected.value), tone: 'info', link: '/invoices',
        note: `${formatInr(s.totalCollected)} all time`, trend: trendOf(st.paymentsCollected.trendPercent), trendTitle: vs(st.paymentsCollected, formatInr),
      },
      {
        id: 'outstanding', label: 'Outstanding payments', value: formatInr(st.outstandingPayments.value), tone: 'warning', link: '/invoices',
        note: 'on unpaid and part-paid invoices', trend: null, trendTitle: '',
      },
      {
        id: 'expenses', label: 'Expenses', value: formatInr(st.expenses.value), tone: 'danger', link: '/company/expenses',
        note: `includes ${formatInr(st.salaryPayouts)} salary`, trend: trendOf(st.expenses.trendPercent, true), trendTitle: vs(st.expenses, formatInr),
      },
      {
        id: 'surplus', label: 'Cash surplus', value: signedInr(st.cashSurplus.value), tone: st.cashSurplus.value < 0 ? 'danger' : 'success', link: '/company/overview',
        note: 'revenue - expenses - salary', trend: trendOf(st.cashSurplus.trendPercent), trendTitle: vs(st.cashSurplus, signedInr),
      },
    ];
  });

  protected readonly revenueChart = computed(() => revenueExpenseOptions(this.summary()?.revenueVsExpenses ?? [], this.colors));
  protected readonly registrationChart = computed(() => registrationOptions(this.summary()?.schoolRegistrations ?? [], this.colors));
  protected readonly statusChart = computed(() => {
    const s = this.summary();
    return statusDonutOptions({ trial: s?.trialCount ?? 0, active: s?.activeCount ?? 0, overdue: s?.overdueCount ?? 0, cancelled: s?.cancelledCount ?? 0 }, this.colors);
  });
  protected readonly statusCount = (key: 'trial' | 'active' | 'overdue' | 'cancelled'): number => {
    const s = this.summary();
    return !s ? 0 : key === 'trial' ? s.trialCount : key === 'active' ? s.activeCount : key === 'overdue' ? s.overdueCount : s.cancelledCount;
  };

  protected readonly segments = computed(() => collectionSegments(this.summary()?.paymentCollection ?? []));
  protected readonly invoicedTotal = computed(() => this.segments().reduce((sum, b) => sum + b.amount, 0));
  protected readonly invoiceCount = computed(() => this.segments().reduce((sum, b) => sum + b.count, 0));

  protected readonly schoolColumns: TableColumn<SchoolRow>[] = [
    { key: 'name', label: 'School' },
    { key: 'schoolId', label: 'School ID' },
    { key: 'boardType', label: 'Board / type' },
    { key: 'registered', label: 'Registered' },
    { key: 'status', label: 'Status', type: 'badge', tone: (v) => (v === 'Active' ? 'green' : 'gray') },
  ];
  protected readonly schoolRows = computed<SchoolRow[]>(() =>
    (this.summary()?.recentSchools ?? []).map((r) => ({
      name: r.schoolName, schoolId: r.schoolId, boardType: r.boardType ?? r.schoolType ?? '-', registered: this.date(r.registeredOn), status: r.status ?? '-',
    })),
  );

  protected readonly transactionColumns: TableColumn<TransactionRow>[] = [
    { key: 'reference', label: 'Reference' },
    { key: 'school', label: 'School' },
    { key: 'date', label: 'Date' },
    { key: 'amount', label: 'Amount' },
    { key: 'method', label: 'Method' },
    { key: 'status', label: 'Status', type: 'badge', tone: (v) => (v === 'Active' ? 'green' : 'gray-strike') },
  ];
  protected readonly transactionRows = computed<TransactionRow[]>(() =>
    (this.summary()?.recentTransactions ?? []).map((t) => ({
      reference: t.referenceNumber || `#${t.paymentId}`, school: t.schoolName, date: this.date(t.paymentDate), amount: formatInr(t.amount), method: t.paymentMode, status: t.status,
    })),
  );

  ngOnInit(): void {
    // one reload when either date changes (changing both quickly is still one request)
    merge(this.fromControl.valueChanges, this.toControl.valueChanges).pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.reload());
    this.load(true);
  }

  private reload(): void {
    const from = this.fromControl.value;
    const to = this.toControl.value;
    this.invalidRange.set(!!from && !!to && from > to);
    if (!this.invalidRange()) this.load(false);
  }

  /** `initial`: ask for the server's default period (this month to date) and show what it chose in the date boxes. */
  protected load(initial: boolean): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.request = this.service
      .summary(initial ? '' : this.fromControl.value, initial ? '' : this.toControl.value)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (s) => {
          this.summary.set(s);
          this.summaryFailed.set(false);
          if (initial) {
            this.fromControl.setValue(s.from, { emitEvent: false });
            this.toControl.setValue(s.to, { emitEvent: false });
            this.invalidRange.set(false);
          }
        },
        error: () => this.summaryFailed.set(true),
      });
  }

  protected resetRange(): void { this.load(true); }

  protected addSchool(): void { void this.router.navigate(['/schools/add']); }
}
