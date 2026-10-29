import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription, debounceTime, finalize, merge } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, InputComponent, PageHeaderComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { CompanyFinanceOverview } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

interface MonthRow {
  label: string;
  revenue: string;
  expenses: string;
  salaryPayouts: string;
  netProfit: string;
  [key: string]: unknown;
}

/**
 * /company/overview (Super Admin): SMSS's own revenue, expenses, salary payouts and net profit for a date range, as numbers (the records live on
 * /invoices, /company/expenses and /company/employees, which the figures link to), plus the same three figures month by month.
 */
@Component({
  selector: 'app-company-overview-page',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, CardComponent, InputComponent, PageHeaderComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './overview-page.component.scss',
  templateUrl: './overview-page.component.html',
})
export class CompanyOverviewPageComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Company Finance' }, { label: 'Overview' }];
  /** Rupees with the minus sign in front ("-₹100.00"), for a loss. */
  protected readonly inr = (n: number): string => (n < 0 ? '-' + formatInr(-n) : formatInr(n));
  protected readonly date = formatDate;

  protected readonly fromControl = new FormControl('', { nonNullable: true });
  protected readonly toControl = new FormControl('', { nonNullable: true });
  protected readonly data = signal<CompanyFinanceOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private request?: Subscription;

  protected readonly invalidRange = signal(false);

  protected readonly columns: TableColumn<MonthRow>[] = [
    { key: 'label', label: 'Month' },
    { key: 'revenue', label: 'Revenue' },
    { key: 'expenses', label: 'Expenses' },
    { key: 'salaryPayouts', label: 'Salary payouts' },
    { key: 'netProfit', label: 'Net profit' },
  ];
  protected readonly rows = computed<MonthRow[]>(() =>
    (this.data()?.months ?? []).map((m) => ({
      label: m.label, revenue: this.inr(m.revenue), expenses: this.inr(m.expenses), salaryPayouts: this.inr(m.salaryPayouts), netProfit: this.inr(m.netProfit),
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

  /** `initial`: ask for the server's default range (1 January of this year .. today) and show what it chose in the date boxes. */
  protected load(initial: boolean): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.request = this.service
      .overview(initial ? '' : this.fromControl.value, initial ? '' : this.toControl.value)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (o) => {
          this.data.set(o);
          this.failed.set(false);
          if (initial) {
            this.fromControl.setValue(o.from, { emitEvent: false });
            this.toControl.setValue(o.to, { emitEvent: false });
          }
        },
        error: () => this.failed.set(true),
      });
  }
}
