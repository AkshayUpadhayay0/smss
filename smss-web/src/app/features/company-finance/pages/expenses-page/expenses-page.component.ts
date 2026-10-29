import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import {
  ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, SelectOption, TableColumn, TableComponent,
} from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { ExpenseFormModalComponent } from '../../components/expense-form-modal/expense-form-modal.component';
import { VoidReasonModalComponent } from '../../components/void-reason-modal/void-reason-modal.component';
import { CompanyExpense, ExpenseCategory, RecordStatus } from '../../models/company-finance.model';
import { CompanyFinanceService } from '../../services/company-finance.service';

interface ExpenseRow extends CompanyExpense {
  dateText: string;
  amountText: string;
  descriptionText: string;
  [key: string]: unknown;
}

const STATUS_FILTERS: SelectOption[] = [
  { label: 'All expenses', value: '' },
  { label: 'Active', value: 'Active' },
  { label: 'Voided', value: 'Voided' },
];
const KNOWN_STATUSES = ['Active', 'Voided'];
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

/**
 * /company/expenses (Super Admin): SMSS's own expenses. Add, void (never delete), filter by category, dates and status. Query parameters from the
 * overview pre-select the filters: `from`, `to`, `categoryId`, `status`.
 */
@Component({
  selector: 'app-company-expenses-page',
  imports: [
    ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent,
    ExpenseFormModalComponent, VoidReasonModalComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './expenses-page.component.scss',
  templateUrl: './expenses-page.component.html',
})
export class CompanyExpensesPageComponent implements OnInit {
  private readonly service = inject(CompanyFinanceService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly from = input<string>();
  readonly to = input<string>();
  readonly categoryId = input<string>();
  readonly status = input<string>();

  protected readonly breadcrumbs = [{ label: 'Company Finance' }, { label: 'Expenses' }];
  protected readonly statusFilters = STATUS_FILTERS;
  protected readonly inr = formatInr;

  protected readonly categoryControl = new FormControl('', { nonNullable: true });
  protected readonly fromControl = new FormControl('', { nonNullable: true });
  protected readonly toControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl<RecordStatus | ''>('', { nonNullable: true });
  protected readonly categories = signal<ExpenseCategory[]>([]);
  protected readonly categoryOptions = computed<SelectOption[]>(() => [{ label: 'All categories', value: '' }, ...this.categories().map((c) => ({ label: c.categoryName, value: String(c.categoryId) }))]);

  protected readonly expenses = signal<CompanyExpense[]>([]);
  protected readonly total = signal(0);
  protected readonly activeTotal = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly invalidRange = signal(false);
  private request?: Subscription;

  protected readonly adding = signal(false);
  protected readonly voiding = signal<CompanyExpense | null>(null);
  protected readonly voidBusy = signal(false);

  protected readonly columns: TableColumn<ExpenseRow>[] = [
    { key: 'dateText', label: 'Date' },
    { key: 'categoryName', label: 'Category' },
    { key: 'descriptionText', label: 'Description' },
    { key: 'amountText', label: 'Amount' },
    { key: 'paymentMode', label: 'Mode' },
    { key: 'recordedByName', label: 'Recorded by' },
    { key: 'status', label: 'Status', type: 'badge', tone: (v) => (v === 'Active' ? 'green' : 'gray-strike') },
  ];
  protected readonly rows = computed<ExpenseRow[]>(() =>
    this.expenses().map((x) => ({
      ...x,
      dateText: formatDate(x.expenseDate),
      amountText: formatInr(x.amount),
      descriptionText: x.status === 'Voided' ? `${x.description} (voided: ${x.voidReason ?? ''})` : x.description,
    })),
  );

  private started = false;
  /** The query-string filters that the controls currently reflect. */
  private appliedQuery = '';

  constructor() {
    // The router reuses this page when only the query string changes (e.g. the overview card, or the sidebar link, while already here): follow it
    effect(() => {
      const key = this.queryKey();
      untracked(() => {
        if (!this.started || key === this.appliedQuery) return;
        this.applyQuery(key);
        this.filter();
      });
    });
  }

  private queryKey(): string {
    return [this.from() ?? '', this.to() ?? '', this.categoryId() ?? '', this.status() ?? ''].join('|');
  }

  private applyQuery(key: string): void {
    this.appliedQuery = key;
    const [from, to, categoryId, status] = key.split('|');
    this.fromControl.setValue(ISO_DATE.test(from) ? from : '', { emitEvent: false });
    this.toControl.setValue(ISO_DATE.test(to) ? to : '', { emitEvent: false });
    this.categoryControl.setValue(/^\d+$/.test(categoryId) ? categoryId : '', { emitEvent: false });
    this.statusControl.setValue(KNOWN_STATUSES.includes(status) ? (status as RecordStatus) : '', { emitEvent: false });
    this.invalidRange.set(false);
  }

  ngOnInit(): void {
    this.applyQuery(this.queryKey());
    this.started = true;
    for (const control of [this.categoryControl, this.fromControl, this.toControl, this.statusControl]) {
      control.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filter());
    }
    this.service.categories().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.categories.set(c), error: () => undefined });
    this.load();
  }

  private filter(): void {
    const from = this.fromControl.value;
    const to = this.toControl.value;
    this.invalidRange.set(!!from && !!to && from > to);
    if (this.invalidRange()) return;
    this.page.set(1);
    this.load();
  }

  protected load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.request = this.service
      .expenses(this.categoryControl.value, this.fromControl.value, this.toControl.value, this.statusControl.value, this.page(), this.pageSize())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => { this.expenses.set(p.items); this.total.set(p.total); this.activeTotal.set(p.activeTotal); this.failed.set(false); },
        error: () => { this.expenses.set([]); this.total.set(0); this.activeTotal.set(0); this.failed.set(true); },
      });
  }

  protected onPage(page: number): void { this.page.set(page); this.load(); }
  protected onPageSize(size: number): void { this.pageSize.set(size); this.page.set(1); this.load(); }

  protected onAdded(): void {
    this.adding.set(false);
    this.load();
  }

  protected confirmVoid(reason: string): void {
    const expense = this.voiding();
    if (!expense || this.voidBusy()) return;
    this.voidBusy.set(true);
    this.service
      .voidExpense(expense.expenseId, reason)
      .pipe(finalize(() => this.voidBusy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Expense voided.');
          this.voiding.set(null);
          this.load();
        },
        error: () => undefined,
      });
  }
}
