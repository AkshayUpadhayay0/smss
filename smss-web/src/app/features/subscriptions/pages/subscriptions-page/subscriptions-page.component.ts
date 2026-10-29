import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription, debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { formatInr } from '../../../../core/utils/currency';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SelectComponent,
  SelectOption,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { ConfigureBillingModalComponent } from '../../components/configure-billing-modal/configure-billing-modal.component';
import { SchoolSubscriptionRow, SubscriptionPricing, SubscriptionStatus, subscriptionStatusLabel } from '../../models/super-admin-dashboard.model';
import { SuperAdminDashboardService } from '../../services/super-admin-dashboard.service';

/** One row of the schools table, with the display strings worked out once. */
interface SchoolRow extends SchoolSubscriptionRow {
  statusText: string;
  headcountText: string;
  termsText: string;
  renewalText: string;
  [key: string]: unknown;
}

/** "Global" for a school on the standard terms, otherwise what is special about it. */
function termsText(s: SchoolSubscriptionRow): string {
  const parts: string[] = [];
  if (s.customRatePerStudentMonthly !== null) parts.push(`${formatInr(s.customRatePerStudentMonthly)} rate`);
  if (s.customYearlyDiscountPercent !== null) parts.push(`${s.customYearlyDiscountPercent}% yearly`);
  if (s.includeStaffInBilling) parts.push('+ staff');
  return parts.length ? parts.join(', ') : 'Global';
}

const STATUS_FILTERS: SelectOption[] = [
  { label: 'All subscriptions', value: '' },
  { label: 'Trial', value: 'TRIAL' },
  { label: 'Active', value: 'ACTIVE' },
  { label: 'Overdue', value: 'OVERDUE' },
  { label: 'Cancelled', value: 'CANCELLED' },
  { label: 'Not set up', value: 'NOT_SET' },
];
const KNOWN_STATUSES = STATUS_FILTERS.map((o) => String(o.value)).filter(Boolean);

// The pill colour is picked from the text shown in the cell
const STATUS_TEXT_TONE: Record<string, string> = {
  Trial: 'blue', 'Trial expired': 'yellow', Active: 'green', Overdue: 'red', Cancelled: 'gray-strike', 'Not set up': 'gray',
};

/**
 * /subscriptions (Super Admin): every school with its subscription, live head counts, terms and renewal, and the billing actions
 * (configure billing, edit terms, generate an invoice). Separate from "Registered Schools", which is the school's profile.
 * `status` (a query parameter, e.g. from a dashboard card) pre-selects the status filter.
 */
@Component({
  selector: 'app-subscriptions-page',
  imports: [
    ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, SelectComponent, TableComponent,
    ConfigureBillingModalComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './subscriptions-page.component.scss',
  templateUrl: './subscriptions-page.component.html',
})
export class SubscriptionsPageComponent implements OnInit {
  private readonly service = inject(SuperAdminDashboardService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  /** Route query parameter ?status=TRIAL|ACTIVE|OVERDUE|CANCELLED|NOT_SET (ignored when it is anything else). */
  readonly status = input<string>();

  protected readonly breadcrumbs = [{ label: 'Subscriptions' }];
  protected readonly statusFilters = STATUS_FILTERS;

  protected readonly searchControl = new FormControl('', { nonNullable: true });
  protected readonly statusControl = new FormControl<SubscriptionStatus | ''>('', { nonNullable: true });
  protected readonly schools = signal<SchoolSubscriptionRow[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  private request?: Subscription;

  /** The global pricing, shown as the placeholder of the custom fields in the configure / terms dialog. */
  protected readonly pricing = signal<SubscriptionPricing | null>(null);
  /** The configure dialog (leaving the trial) and the terms dialog (renegotiating) are one component in two modes. */
  protected readonly configuring = signal<{ school: SchoolSubscriptionRow; mode: 'configure' | 'terms' } | null>(null);
  protected readonly generatingFor = signal<string | null>(null);

  protected readonly columns: TableColumn<SchoolRow>[] = [
    { key: 'schoolName', label: 'School' },
    { key: 'schoolCode', label: 'Code' },
    { key: 'statusText', label: 'Subscription', type: 'badge', tone: (v) => STATUS_TEXT_TONE[String(v)] ?? 'gray' },
    { key: 'billableUserCount', label: 'Billed users' },
    { key: 'headcountText', label: 'Students / teachers / staff' },
    { key: 'termsText', label: 'Terms' },
    { key: 'renewalText', label: 'Next renewal' },
  ];
  protected readonly rows = computed<SchoolRow[]>(() =>
    this.schools().map((s) => ({
      ...s,
      statusText: s.trialExpired ? 'Trial expired' : subscriptionStatusLabel(s.subscriptionStatus),
      headcountText: `${s.studentCount} / ${s.teacherCount} / ${s.staffCount}`,
      termsText: termsText(s),
      renewalText: s.nextRenewalDate ? formatDate(s.nextRenewalDate) : s.subscriptionStatus === 'TRIAL' ? `Trial ends ${formatDate(s.trialEndDate)}` : '—',
    })),
  );

  constructor() {
    // The router reuses this page when only the query string changes (a dashboard card, or the sidebar link, while already here): follow it
    effect(() => {
      const wanted = this.statusFromQuery(this.status());
      untracked(() => {
        if (this.started && wanted !== this.statusControl.value) this.statusControl.setValue(wanted);
      });
    });
  }

  private started = false;

  private statusFromQuery(value: string | undefined): SubscriptionStatus | '' {
    const upper = (value ?? '').toUpperCase();
    return KNOWN_STATUSES.includes(upper) ? (upper as SubscriptionStatus) : '';
  }

  ngOnInit(): void {
    this.statusControl.setValue(this.statusFromQuery(this.status()), { emitEvent: false });
    this.started = true;

    this.searchControl.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filter());
    this.statusControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.filter());
    this.service.summary().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (s) => this.pricing.set(s.pricing), error: () => undefined });
    this.load();
  }

  protected load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.request = this.service
      .schools(this.searchControl.value.trim(), this.statusControl.value, this.page(), this.pageSize())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => { this.schools.set(p.items); this.total.set(p.total); this.failed.set(false); },
        error: () => { this.schools.set([]); this.total.set(0); this.failed.set(true); },
      });
  }

  private filter(): void {
    this.page.set(1);
    this.load();
  }

  protected onPage(page: number): void { this.page.set(page); this.load(); }
  protected onPageSize(size: number): void { this.pageSize.set(size); this.page.set(1); this.load(); }

  // ---------------------------------------------------------------- the existing school screens
  protected addSchool(): void { void this.router.navigate(['/schools/add']); }
  protected allSchools(): void { void this.router.navigate(['/schools']); }
  protected viewSchool(s: SchoolSubscriptionRow): void { void this.router.navigate(['/schools', s.schoolId, 'view']); }
  protected editSchool(s: SchoolSubscriptionRow): void { void this.router.navigate(['/schools', s.schoolId, 'edit']); }

  /** The Invoices page, narrowed to this school. */
  protected showInvoicesOf(s: SchoolSubscriptionRow): void {
    void this.router.navigate(['/invoices'], { queryParams: { schoolId: s.schoolId, schoolName: s.schoolName } });
  }

  // ---------------------------------------------------------------- what a school row can do
  protected canConfigure(s: SchoolSubscriptionRow): boolean { return s.subscriptionStatus === 'TRIAL'; }
  protected canEditTerms(s: SchoolSubscriptionRow): boolean { return s.subscriptionStatus === 'ACTIVE' || s.subscriptionStatus === 'OVERDUE'; }
  protected canGenerate(s: SchoolSubscriptionRow): boolean {
    return (s.subscriptionStatus === 'ACTIVE' || s.subscriptionStatus === 'OVERDUE') && !!s.billingFrequency;
  }

  protected async generate(s: SchoolSubscriptionRow): Promise<void> {
    if (this.generatingFor()) return;
    const ok = await this.confirm.confirm({
      title: 'Generate invoice',
      message: `Generate the next ${s.billingFrequency === 'YEARLY' ? 'yearly' : 'monthly'} invoice for ${s.schoolName}? The teacher, staff and student counts are taken now and locked on the invoice.`,
      confirmText: 'Generate',
    });
    if (!ok) return;
    this.generatingFor.set(s.schoolId);
    this.service
      .generateInvoice(s.schoolId)
      .pipe(finalize(() => this.generatingFor.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (inv) => {
          this.toast.success(`Invoice #${inv.invoiceId} generated: ${formatInr(inv.netAmount)} for ${inv.billableUserCount} billed ${inv.billableUserCount === 1 ? 'user' : 'users'}.`);
          this.load();
        },
        error: () => undefined, // the server's reason (e.g. an unpaid invoice already exists) is toasted by the interceptor
      });
  }

  protected onConfigured(): void {
    this.configuring.set(null);
    this.load();
  }
}
