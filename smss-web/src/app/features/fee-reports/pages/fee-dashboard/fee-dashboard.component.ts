import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { formatInr } from '../../../../core/utils/currency';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { Section } from '../../../school-setup/models/section.model';
import { FeeDashboard } from '../../models/fee-report.model';
import { FeeReportService } from '../../services/fee-report.service';

const DAY_OPTIONS: SelectOption[] = [7, 15, 30, 60].map((d) => ({ label: `Next ${d} days`, value: String(d) }));

/** Collected / Outstanding / Overdue / Upcoming for an academic year, optionally narrowed to a class and section. */
@Component({
  selector: 'app-fee-dashboard',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-dashboard.component.scss',
  templateUrl: './fee-dashboard.component.html',
})
export class FeeDashboardComponent implements OnInit {
  private readonly reports = inject(FeeReportService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Fees' }, { label: 'Fee Dashboard' }];
  protected readonly dayOptions = DAY_OPTIONS;
  protected readonly inr = formatInr;
  protected readonly date = formatDate;

  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly daysControl = new FormControl('7', { nonNullable: true });

  protected readonly yearOptions = signal<SelectOption[]>([]);
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly allSections = signal<Section[]>([]);
  private readonly classValue = signal('');
  protected readonly sectionOptions = computed<SelectOption[]>(() =>
    this.allSections()
      .filter((s) => String(s.classId) === this.classValue() && s.statusName === 'Active')
      .map((s) => ({ label: s.sectionName, value: String(s.sectionId) })),
  );

  protected readonly data = signal<FeeDashboard | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly setupLoading = signal(true);

  /** The month with the most money, so the bars share one scale. */
  protected readonly maxMonth = computed(() => Math.max(1, ...(this.data()?.collectionByMonth ?? []).map((m) => m.amount)));

  ngOnInit(): void {
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.daysControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.classValue.set(v);
      this.sectionControl.setValue('', { emitEvent: false }); // a section belongs to one class
      this.load();
    });

    this.reports
      .filterOptions()
      .pipe(finalize(() => this.setupLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe(({ years, classes, sections }) => {
        const active = years.filter((y) => y.statusName === 'Active');
        this.yearOptions.set(active.map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })));
        this.classOptions.set([{ label: 'All classes', value: '' }, ...classes.filter((c) => c.statusName === 'Active').map((c) => ({ label: c.className, value: String(c.classId) }))]);
        this.allSections.set(sections);
        const year = active.find((y) => y.isCurrent) ?? active[0];
        if (year) this.yearControl.setValue(String(year.academicYearId));
      });
  }

  protected load(): void {
    if (!this.yearControl.value) return;
    this.loading.set(true);
    this.failed.set(false);
    this.reports
      .dashboard(Number(this.yearControl.value), Number(this.classControl.value) || null, Number(this.sectionControl.value) || null, Number(this.daysControl.value))
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => this.data.set(d),
        error: () => this.failed.set(true), // already toasted by the interceptor
      });
  }

  protected barWidth(amount: number): string {
    return `${Math.round((amount / this.maxMonth()) * 100)}%`;
  }

  /** Drill-down (managers only): the class-wise report for the same filter. */
  protected openClassReport(): void {
    void this.router.navigate(['/fee-reports/class'], {
      queryParams: { academicYearId: this.yearControl.value, classId: this.classControl.value || null, sectionId: this.sectionControl.value || null },
    });
  }
}
