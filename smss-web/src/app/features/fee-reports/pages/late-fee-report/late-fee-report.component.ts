import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize, firstValueFrom } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableColumn, TableComponent } from '../../../../shared/components';
import { SchoolClass } from '../../../school-setup/models/class.model';
import { ClassService } from '../../../school-setup/services/class.service';
import { LateFeeReport, csvCell } from '../../models/fee-report.model';
import { FeeReportService } from '../../services/fee-report.service';

interface ChargedRow extends Record<string, unknown> { paymentDate: string; receiptNumber: string; student: string; className: string; feeType: string; dueDate: string; daysLate: string; amount: string }
interface WaivedRow extends Record<string, unknown> { paymentDate: string; receiptNumber: string; student: string; className: string; wouldHaveBeen: string; waivedBy: string; note: string }

const CHARGED: TableColumn<ChargedRow>[] = [
  { key: 'paymentDate', label: 'Paid on' }, { key: 'receiptNumber', label: 'Receipt' }, { key: 'student', label: 'Student' }, { key: 'className', label: 'Class' },
  { key: 'feeType', label: 'Fee' }, { key: 'dueDate', label: 'Due date' }, { key: 'daysLate', label: 'Days late' }, { key: 'amount', label: 'Late fee' },
];
const WAIVED: TableColumn<WaivedRow>[] = [
  { key: 'paymentDate', label: 'Paid on' }, { key: 'receiptNumber', label: 'Receipt' }, { key: 'student', label: 'Student' }, { key: 'className', label: 'Class' },
  { key: 'wouldHaveBeen', label: 'Would have been' }, { key: 'waivedBy', label: 'Waived by' }, { key: 'note', label: 'Note' },
];

/** Every export is capped so a very busy school cannot freeze the browser: 100 rows per request, at most this many rows. */
const EXPORT_CAP = 10000;
const EXPORT_PAGE = 100;

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const when = (value: string) => new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });

/**
 * The Late fee report (School Admin, Accountant, Principal read-only): late fees charged at payment, one row per payment and installment with the days late, filterable by date range and class, paged by the server,
 * with a CSV export built in the browser; and, apart, the waived ones (what they would have been, who waived, the note). Voided payments are in neither list.
 */
@Component({
  selector: 'app-late-fee-report',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './late-fee-report.component.scss',
  templateUrl: './late-fee-report.component.html',
})
export class LateFeeReportComponent implements OnInit {
  private readonly reports = inject(FeeReportService);
  private readonly classApi = inject(ClassService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Fees' }, { label: 'Late fee report' }];
  protected readonly inr = formatInr;

  private readonly today = new Date();
  protected readonly fromControl = new FormControl(iso(new Date(this.today.getFullYear(), this.today.getMonth(), 1)), { nonNullable: true });
  protected readonly toControl = new FormControl(iso(this.today), { nonNullable: true });
  private readonly range = signal({ from: this.fromControl.value, to: this.toControl.value });

  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly classId = signal<number | null>(null);
  protected readonly view = signal<'charged' | 'waived'>('charged');
  protected readonly report = signal<LateFeeReport | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly exporting = signal(false);

  protected readonly rangeError = computed(() => (this.range().from && this.range().to && this.range().to < this.range().from ? 'The end date can\'t be before the start date' : undefined));
  protected readonly total = computed(() => this.report()?.totalItems ?? 0);
  protected readonly chargedCount = computed(() => this.report()?.chargedCount ?? 0);
  protected readonly chargedAmount = computed(() => this.report()?.chargedAmount ?? 0);
  protected readonly waivedCount = computed(() => this.report()?.waivedCount ?? 0);
  protected readonly waivedAmount = computed(() => this.report()?.waivedAmount ?? 0);
  protected readonly columns = computed(() => (this.view() === 'charged' ? CHARGED : WAIVED) as TableColumn<Record<string, unknown>>[]);
  protected readonly rows = computed<Record<string, unknown>[]>(() => {
    const r = this.report();
    if (!r) return [];
    if (this.view() === 'charged')
      return r.charged.map((c): ChargedRow => ({ paymentDate: when(c.paymentDate), receiptNumber: c.receiptNumber, student: `${c.studentName} (${c.studentId})`, className: c.className ?? '—', feeType: c.feeType, dueDate: c.dueDate, daysLate: c.daysLate === c.daysCharged ? String(c.daysLate) : `${c.daysLate} (${c.daysCharged} charged)`, amount: formatInr(c.amount) }));
    return r.waived.map((x): WaivedRow => ({ paymentDate: when(x.paymentDate), receiptNumber: x.receiptNumber, student: `${x.studentName} (${x.studentId})`, className: x.className ?? '—', wouldHaveBeen: formatInr(x.wouldHaveBeen), waivedBy: x.waivedBy ?? '—', note: x.note ?? '—' }));
  });

  ngOnInit(): void {
    this.fromControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.range.update((r) => ({ ...r, from: v })));
    this.toControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => this.range.update((r) => ({ ...r, to: v })));
    this.classApi.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.classes.set([...c].sort((a, b) => a.sequenceOrder - b.sequenceOrder)), error: () => this.classes.set([]) });
    this.load();
  }

  protected apply(): void {
    if (this.rangeError()) return;
    this.page.set(1);
    this.load();
  }

  protected pickClass(v: string): void {
    this.classId.set(v === '' ? null : Number(v));
    this.apply();
  }

  protected setView(v: 'charged' | 'waived'): void {
    if (this.view() === v) return;
    this.view.set(v);
    this.page.set(1);
    this.load();
  }

  protected onPage(p: number): void { this.page.set(p); this.load(); }
  protected onPageSize(n: number): void { this.pageSize.set(n); this.page.set(1); this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.reports
      .lateFeeReport(this.fromControl.value || null, this.toControl.value || null, this.classId(), this.view(), this.page(), this.pageSize())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.report.set(r), error: () => { this.report.set(null); this.failed.set(true); } });
  }

  /** Every row of the current range, class and list (not only this page), built in the browser. */
  protected async exportCsv(): Promise<void> {
    if (this.exporting() || this.total() === 0) return;
    this.exporting.set(true);
    try {
      const view = this.view();
      const lines: string[][] = view === 'charged'
        ? [['Paid on', 'Receipt', 'Student ID', 'Student', 'Class', 'Fee', 'Due date', 'Days late', 'Days charged', 'Applied to the due', 'Late fee']]
        : [['Paid on', 'Receipt', 'Student ID', 'Student', 'Class', 'Would have been', 'Waived by', 'Note']];
      let fetched = 0;
      for (let p = 1; fetched < Math.min(this.total(), EXPORT_CAP); p++) {
        const res = await firstValueFrom(this.reports.lateFeeReport(this.fromControl.value || null, this.toControl.value || null, this.classId(), view, p, EXPORT_PAGE));
        const rows = view === 'charged' ? res.charged : res.waived;
        fetched += rows.length;
        if (view === 'charged') for (const c of res.charged) lines.push([when(c.paymentDate), c.receiptNumber, c.studentId, c.studentName, c.className ?? '', c.feeType, c.dueDate, String(c.daysLate), String(c.daysCharged), String(c.baseAmount), String(c.amount)]);
        else for (const x of res.waived) lines.push([when(x.paymentDate), x.receiptNumber, x.studentId, x.studentName, x.className ?? '', String(x.wouldHaveBeen), x.waivedBy ?? '', x.note ?? '']);
        if (rows.length < EXPORT_PAGE) break;
      }
      const blob = new Blob(['\ufeff' + lines.map((l) => l.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `late-fee-${view}-${this.fromControl.value || 'start'}-${this.toControl.value || 'today'}.csv`.replace(/[^\w.-]+/g, '_');
      a.click();
      URL.revokeObjectURL(url);
      if (this.total() > EXPORT_CAP) this.toast.info(`The export holds the newest ${EXPORT_CAP} rows. Narrow the dates to export the rest.`);
    } catch {
      // the interceptor already toasted the reason
    } finally {
      this.exporting.set(false);
    }
  }
}
