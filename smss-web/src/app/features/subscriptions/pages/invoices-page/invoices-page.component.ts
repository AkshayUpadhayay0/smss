import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, PaginationComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { InvoiceDetailModalComponent } from '../../components/invoice-detail-modal/invoice-detail-modal.component';
import { RecordPaymentModalComponent } from '../../components/record-payment-modal/record-payment-modal.component';
import { InvoiceStatus, SubscriptionInvoice, invoiceTone } from '../../models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../services/super-admin-dashboard.service';

interface InvoiceRow extends SubscriptionInvoice {
  periodText: string;
  dueText: string;
  netText: string;
  paidText: string;
  balanceText: string;
  [key: string]: unknown;
}

const INVOICE_FILTERS: SelectOption[] = [
  { label: 'All invoices', value: '' },
  { label: 'Pending', value: 'Pending' },
  { label: 'Overdue', value: 'Overdue' },
  { label: 'Paid', value: 'Paid' },
];
const KNOWN_STATUSES = ['Pending', 'Overdue', 'Paid'];

/**
 * /invoices (Super Admin): every subscription invoice, filterable by school and status, with the invoice detail (counts, billed users, rate,
 * payments, void) and Record payment. Query parameters from other pages pre-select the filters:
 * `schoolId` (+ `schoolName`, shown even if the school is not among the first 100) and `status`.
 */
@Component({
  selector: 'app-invoices-page',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent, InvoiceDetailModalComponent, RecordPaymentModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './invoices-page.component.scss',
  templateUrl: './invoices-page.component.html',
})
export class InvoicesPageComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly destroyRef = inject(DestroyRef);

  readonly schoolId = input<string>();
  readonly schoolName = input<string>();
  /** Route query parameter ?status=Pending|Overdue|Paid (ignored when it is anything else). */
  readonly status = input<string>();

  protected readonly breadcrumbs = [{ label: 'Invoices' }];
  protected readonly invoiceFilters = INVOICE_FILTERS;

  protected readonly statusControl = new FormControl<InvoiceStatus | ''>('', { nonNullable: true });
  protected readonly schoolControl = new FormControl('', { nonNullable: true });
  protected readonly schoolOptions = signal<SelectOption[]>([{ label: 'All schools', value: '' }]);

  protected readonly invoices = signal<SubscriptionInvoice[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private request?: Subscription;

  protected readonly detailInvoiceId = signal<number | null>(null);
  /** The payment dialog is for a school; `invoiceId` pre-selects the invoice it was opened from. */
  protected readonly paying = signal<{ schoolId: string; schoolName: string; invoiceId: number | null } | null>(null);
  /** Set when the payment dialog was opened from the invoice dialog, so that one comes back when this one closes. */
  private returnToInvoice: number | null = null;

  protected readonly columns: TableColumn<InvoiceRow>[] = [
    { key: 'invoiceId', label: 'Invoice' },
    { key: 'schoolName', label: 'School' },
    { key: 'periodText', label: 'Period' },
    { key: 'billableUserCount', label: 'Billed users' },
    { key: 'netText', label: 'Amount' },
    { key: 'paidText', label: 'Paid' },
    { key: 'balanceText', label: 'Balance' },
    { key: 'dueText', label: 'Due' },
    { key: 'status', label: 'Status', type: 'badge', tone: invoiceTone },
  ];
  protected readonly rows = computed<InvoiceRow[]>(() =>
    this.invoices().map((i) => ({
      ...i,
      periodText: `${formatDate(i.billingPeriodStart)} – ${formatDate(i.billingPeriodEnd)}`,
      dueText: formatDate(i.dueDate),
      netText: formatInr(i.netAmount),
      paidText: formatInr(i.paidAmount),
      balanceText: formatInr(i.balanceAmount),
    })),
  );

  constructor() {
    // The router reuses this page when only the query string changes (e.g. the sidebar link while already here): follow it
    effect(() => {
      const status = this.statusFromQuery(this.status());
      const school = this.schoolId() ?? '';
      const name = this.schoolName();
      untracked(() => {
        if (!this.started || this.appliedQuery === `${status}|${school}`) return; // nothing new (also the first run, already applied by ngOnInit)
        this.appliedQuery = `${status}|${school}`;
        if (school && !this.schoolOptions().some((o) => o.value === school)) this.schoolOptions.update((o) => [...o, { label: name || school, value: school }]);
        this.statusControl.setValue(status, { emitEvent: false });
        this.schoolControl.setValue(school, { emitEvent: false });
        this.filter(); // one reload for both changes
      });
    });
  }

  private started = false;
  /** The status|school from the URL that the filters currently reflect. */
  private appliedQuery = '';

  private statusFromQuery(value: string | undefined): InvoiceStatus | '' {
    return value && KNOWN_STATUSES.includes(value) ? (value as InvoiceStatus) : '';
  }

  ngOnInit(): void {
    this.statusControl.setValue(this.statusFromQuery(this.status()), { emitEvent: false });
    const school = this.schoolId();
    if (school) {
      this.schoolControl.setValue(school, { emitEvent: false });
      this.schoolOptions.set([{ label: 'All schools', value: '' }, { label: this.schoolName() || school, value: school }]);
    }
    this.appliedQuery = `${this.statusControl.value}|${school ?? ''}`;
    this.started = true;

    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filter());
    this.schoolControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filter());

    // The school filter lists the first 100 schools by name (the API's page limit); a school opened from elsewhere is always in the list
    this.service
      .schools('', '', 1, 100)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          const options: SelectOption[] = [{ label: 'All schools', value: '' }, ...p.items.map((s) => ({ label: s.schoolName, value: s.schoolId }))];
          const wanted = this.schoolControl.value;
          if (wanted && !options.some((o) => o.value === wanted)) options.push({ label: this.schoolName() || wanted, value: wanted });
          this.schoolOptions.set(options);
        },
        error: () => undefined, // the filter just keeps what it has
      });
    this.load();
  }

  protected load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.request = this.service
      .invoices(this.schoolControl.value, this.statusControl.value, this.page(), this.pageSize())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => { this.invoices.set(p.items); this.total.set(p.total); this.failed.set(false); },
        error: () => { this.invoices.set([]); this.total.set(0); this.failed.set(true); },
      });
  }

  private filter(): void {
    this.page.set(1);
    this.load();
  }

  protected onPage(page: number): void { this.page.set(page); this.load(); }
  protected onPageSize(size: number): void { this.pageSize.set(size); this.page.set(1); this.load(); }

  // ---------------------------------------------------------------- dialogs
  protected openInvoice(row: SubscriptionInvoice): void { this.detailInvoiceId.set(row.invoiceId); }

  protected payInvoice(row: SubscriptionInvoice): void {
    this.returnToInvoice = null;
    this.paying.set({ schoolId: row.schoolId, schoolName: row.schoolName, invoiceId: row.invoiceId });
  }

  /** The invoice dialog asked to record a payment: swap to the payment dialog, and come back to the invoice when it closes. */
  protected payFromDetail(invoice: SubscriptionInvoice): void {
    this.returnToInvoice = invoice.invoiceId;
    this.detailInvoiceId.set(null);
    this.paying.set({ schoolId: invoice.schoolId, schoolName: invoice.schoolName, invoiceId: invoice.invoiceId });
  }

  protected closePayment(saved: boolean): void {
    this.paying.set(null);
    if (saved) this.load();
    if (this.returnToInvoice !== null) this.detailInvoiceId.set(this.returnToInvoice);
    this.returnToInvoice = null;
  }
}
