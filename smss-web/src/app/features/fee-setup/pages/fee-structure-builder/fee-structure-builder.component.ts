import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { finalize, forkJoin, map, startWith } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { extractErrorMessage } from '../../../../core/interceptors/error.interceptor';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { SchoolClass } from '../../../school-setup/models/class.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { BackdatingDialogComponent } from '../../components/backdating-dialog/backdating-dialog.component';
import { DueRemovalConfirmation, FeeFrequency, FeeStructure, FeeStructureLine, FeeStructureLineRequest, FeeTypeOption } from '../../models/fee-structure.model';
import { FeeStructureService } from '../../services/fee-structure.service';

const MAX_AMOUNT = 9_999_999_999.99;

interface LineControls {
  feeTypeId: FormControl<string>;
  frequencyId: FormControl<string>;
  /** Day of the month (1-31); only enabled (and required) for a recurring frequency. */
  dueDay: FormControl<string>;
  amount: FormControl<string>;
  /** yyyy-MM-dd: from when this fee is charged. */
  effectiveFrom: FormControl<string>;
}

/** Frequencies that fall due on a day of the month. One Time and Annual are a single charge and need no due day. */
const RECURRING_CODES = ['MONTHLY', 'QUARTERLY', 'HALF_YEARLY'];

function dueDayValidator(c: AbstractControl): ValidationErrors | null {
  const raw = String(c.value ?? '').trim();
  if (raw === '') return null; // `required` reports the empty case
  const n = Number(raw);
  return Number.isInteger(n) && n >= 1 && n <= 31 ? null : { amount: 'Enter a day from 1 to 31' };
}
type LineGroup = FormGroup<LineControls>;

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);

/** A non-negative amount with at most two decimals (numeric(12,2) in the database). */
function amountValidator(c: AbstractControl): ValidationErrors | null {
  const raw = String(c.value ?? '').trim();
  if (raw === '') return null; // `required` reports the empty case
  const n = Number(raw);
  if (!Number.isFinite(n) || n < 0) return { amount: 'Enter an amount of 0 or more' };
  if (n > MAX_AMOUNT) return { amount: 'Amount is too large' };
  if (!/^\d+(\.\d{1,2})?$/.test(raw)) return { amount: 'Use at most 2 decimal places' };
  return null;
}

const todayIso = () => {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

const inr = (n: number) => '₹' + n.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * Builds (or edits) one fee structure: academic year + class on top, then one row per fee head
 * (fee type, frequency, amount). Everything is saved in a single request.
 */
@Component({
  selector: 'app-fee-structure-builder',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, SelectComponent, BackdatingDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './fee-structure-builder.component.scss',
  templateUrl: './fee-structure-builder.component.html',
})
export class FeeStructureBuilderComponent implements OnInit {
  private readonly service = inject(FeeStructureService);
  private readonly years = inject(AcademicYearService);
  private readonly classes = inject(ClassService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the :feeStructureId route param; absent when adding. */
  readonly feeStructureId = input<string>();
  protected readonly isEdit = computed(() => !!this.feeStructureId());

  protected readonly form = new FormGroup({
    academicYearId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    classId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    structureName: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(150)] }),
    lines: new FormArray<LineGroup>([]),
  });
  protected get lines(): FormArray<LineGroup> {
    return this.form.controls.lines;
  }

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);

  private readonly allYears = signal<AcademicYear[]>([]);
  private readonly allClasses = signal<SchoolClass[]>([]);
  private readonly feeTypes = signal<FeeTypeOption[]>([]);
  protected readonly frequencies = signal<FeeFrequency[]>([]);
  /** The structure being edited (its year / class may be inactive, but must stay visible). */
  private readonly loaded = signal<FeeStructure | null>(null);

  /** The form's current values, re-read on every change (rows added/removed/edited). */
  protected readonly value = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue()), startWith(this.form.getRawValue())), {
    requireSync: true,
  });

  protected readonly breadcrumbs = computed(() => [
    { label: 'Fee Setup' },
    { label: 'Fee Structures', link: '/fee-setup/fee-structures' },
    { label: this.isEdit() ? 'Edit' : 'Add' },
  ]);

  protected readonly yearOptions = computed<SelectOption[]>(() => {
    const own = this.loaded()?.academicYearId;
    return this.allYears()
      .filter((y) => y.statusName === 'Active' || y.academicYearId === own)
      .map((y) => ({ label: y.yearName + (y.statusName === 'Active' ? '' : ' (inactive)'), value: String(y.academicYearId) }));
  });

  protected readonly classOptions = computed<SelectOption[]>(() => {
    const own = this.loaded()?.classId;
    return [...this.allClasses()]
      .sort((a, b) => a.sequenceOrder - b.sequenceOrder || a.className.localeCompare(b.className))
      .filter((c) => c.statusName === 'Active' || c.classId === own)
      .map((c) => ({ label: c.className + (c.statusName === 'Active' ? '' : ' (inactive)'), value: String(c.classId) }));
  });

  /** Per row: the active fee types, with the ones another row already uses disabled. */
  protected readonly feeTypeOptions = computed<SelectOption[][]>(() => {
    const picked = this.value().lines.map((l) => l.feeTypeId);
    return picked.map((own, i) =>
      this.feeTypes()
        .filter((t) => t.isActive || String(t.feeTypeId) === own)
        .map((t) => ({
          label: t.feeTypeName + (t.isActive ? '' : ' (inactive)'),
          value: String(t.feeTypeId),
          disabled: picked.some((p, j) => j !== i && p === String(t.feeTypeId)),
        })),
    );
  });

  /** The chosen academic year's first and last day (yyyy-MM-dd): every effective date has to fall inside it. */
  protected readonly yearRange = computed(() => this.rangeOf(this.value().academicYearId));

  /** Read straight from the control (not the `value` signal, which lags one change behind inside a valueChanges handler). */
  private currentRange(): { start: string; end: string } | null {
    return this.rangeOf(this.form.controls.academicYearId.getRawValue());
  }

  private rangeOf(yearId: string): { start: string; end: string } | null {
    const y = this.allYears().find((x) => String(x.academicYearId) === yearId);
    return y ? { start: y.startDate.slice(0, 10), end: y.endDate.slice(0, 10) } : null;
  }

  /** Saved lines (by their form group): used for the backdating action and the frequency lock. */
  private readonly savedLines = new Map<LineGroup, FeeStructureLine>();
  /** The line whose "Apply to earlier months" dialog is open. */
  protected readonly backdatingLine = signal<FeeStructureLine | null>(null);

  protected saved(index: number): FeeStructureLine | undefined {
    return this.savedLines.get(this.lines.at(index));
  }

  /** The default date for a NEW line: today when students already have this structure (so adding a fee in September does not bill April to August), otherwise the first day of the year. */
  private defaultEffective(): string {
    const range = this.currentRange();
    if (!range) return '';
    const today = todayIso();
    return this.loaded()?.hasAssignedStudents && today >= range.start && today <= range.end ? today : range.start;
  }

  protected readonly frequencyOptions = computed<SelectOption[]>(() =>
    this.frequencies().map((f) => ({ label: f.frequencyName, value: String(f.frequencyId) })),
  );

  /** Subtotals per frequency: a monthly fee and an annual one are never added together. */
  protected readonly totals = computed(() => {
    const sums = new Map<string, number>();
    for (const l of this.value().lines) {
      const amount = Number(l.amount);
      if (!l.frequencyId || l.amount.trim() === '' || !Number.isFinite(amount)) continue;
      sums.set(l.frequencyId, Math.round(((sums.get(l.frequencyId) ?? 0) + amount) * 100) / 100);
    }
    const names = new Map(this.frequencies().map((f) => [String(f.frequencyId), f.frequencyName]));
    return this.frequencies()
      .map((f) => String(f.frequencyId))
      .filter((id) => sums.has(id))
      .map((id) => ({ frequency: names.get(id) ?? '', amount: inr(sums.get(id)!) }));
  });

  protected readonly hasNoFeeTypes = computed(() => !this.loading() && this.feeTypes().filter((t) => t.isActive).length === 0);

  constructor() {
    // Suggest a name once year and class are chosen — until the user types their own
    const maybeSuggest = () => {
      const name = this.form.controls.structureName;
      if (this.isEdit() || name.dirty) return;
      const cls = this.allClasses().find((c) => String(c.classId) === this.form.controls.classId.value);
      const year = this.allYears().find((y) => String(y.academicYearId) === this.form.controls.academicYearId.value);
      if (cls && year) name.setValue(`${cls.className} Fees ${year.yearName}`);
    };
    this.form.controls.classId.valueChanges.pipe(takeUntilDestroyed()).subscribe(maybeSuggest);
    this.form.controls.academicYearId.valueChanges.pipe(takeUntilDestroyed()).subscribe(maybeSuggest);
    // choosing (or changing) the year gives every still-untouched date the new year's default
    this.form.controls.academicYearId.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      for (const l of this.lines.controls) {
        if (!this.savedLines.has(l) && !l.controls.effectiveFrom.dirty) l.controls.effectiveFrom.setValue(this.defaultEffective());
        l.controls.effectiveFrom.updateValueAndValidity();
      }
    });
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    const id = this.feeStructureId();

    forkJoin({
      years: this.years.list(),
      classes: this.classes.list(),
      feeTypes: this.service.feeTypes(),
      frequencies: this.service.frequencies(),
      structure: id ? this.service.get(Number(id)) : [null],
    })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.allYears.set(r.years);
          this.allClasses.set(r.classes);
          this.feeTypes.set(r.feeTypes);
          this.frequencies.set(r.frequencies);

          const s = r.structure as FeeStructure | null;
          if (s) {
            this.loaded.set(s);
            this.lines.clear();
            this.savedLines.clear();
            this.form.patchValue({ academicYearId: String(s.academicYearId), classId: String(s.classId), structureName: s.structureName });
            for (const d of s.details) {
              const g = this.newLine(String(d.feeTypeId), String(d.frequencyId), String(d.amount), d.dueDay == null ? '' : String(d.dueDay), d.effectiveFrom?.slice(0, 10) ?? '');
              this.savedLines.set(g, d);
              // students already have dues for it: the frequency is locked (a different frequency is a new line with its own date)
              if (d.hasDues) g.controls.frequencyId.disable({ emitEvent: false });
              this.lines.push(g);
            }
            this.form.markAsPristine();
            // A structure belongs to ONE class and year; changing them would be a different structure
            this.form.controls.academicYearId.disable();
            this.form.controls.classId.disable();
          } else if (this.lines.length === 0) {
            this.addLine();
          }
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private newLine(feeTypeId = '', frequencyId = '', amount = '', dueDay = '', effectiveFrom = ''): LineGroup {
    const group = new FormGroup<LineControls>({
      feeTypeId: new FormControl(feeTypeId, { nonNullable: true, validators: [Validators.required] }),
      frequencyId: new FormControl(frequencyId, { nonNullable: true, validators: [Validators.required] }),
      dueDay: new FormControl(dueDay, { nonNullable: true, validators: [Validators.required, dueDayValidator] }),
      amount: new FormControl(amount, { nonNullable: true, validators: [Validators.required, amountValidator] }),
      effectiveFrom: new FormControl(effectiveFrom, { nonNullable: true, validators: [Validators.required, (c: AbstractControl) => this.effectiveValidator(c)] }),
    });
    // The due day only applies to a recurring frequency: enable / require it for those, switch it off (and clear it) otherwise
    const sync = () => {
      const recurring = this.isRecurring(group.controls.frequencyId.value);
      if (recurring && group.controls.dueDay.disabled) group.controls.dueDay.enable({ emitEvent: false });
      if (!recurring && group.controls.dueDay.enabled) {
        group.controls.dueDay.setValue('', { emitEvent: false });
        group.controls.dueDay.disable({ emitEvent: false });
      }
    };
    sync();
    group.controls.frequencyId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      sync();
      this.form.updateValueAndValidity(); // so the totals / validity pick up the enabled state
    });
    return group;
  }

  /** The date must lie inside the academic year of the structure. */
  private effectiveValidator(c: AbstractControl): ValidationErrors | null {
    const v = String(c.value ?? '');
    const range = this.currentRange();
    if (!v || !range) return null; // `required` reports the empty case
    if (v < range.start) return { amount: 'Cannot be before the start of the academic year' };
    if (v > range.end) return { amount: 'Cannot be after the end of the academic year' };
    return null;
  }

  /** True when the frequency (by id) is Monthly, Quarterly or Half-Yearly. */
  protected isRecurring(frequencyId: string): boolean {
    const f = this.frequencies().find((x) => String(x.frequencyId) === frequencyId);
    return !!f && RECURRING_CODES.includes(f.frequencyCode);
  }

  protected addLine(): void {
    this.lines.push(this.newLine('', '', '', '', this.defaultEffective()));
  }

  protected removeLine(index: number): void {
    if (this.lines.length > 1) {
      this.savedLines.delete(this.lines.at(index));
      this.lines.removeAt(index);
    }
  }

  protected error(control: AbstractControl | null): string | undefined {
    if (!control || control.valid || !(control.touched || this.submitted())) return undefined;
    if (control.hasError('required') || control.hasError('blank')) return 'This field is required';
    if (control.hasError('maxlength')) return `Maximum ${control.getError('maxlength').requiredLength} characters`;
    if (control.hasError('amount')) return control.getError('amount');
    return 'Invalid value';
  }

  protected lineError(index: number, name: keyof LineControls): string | undefined {
    return this.error(this.lines.at(index).controls[name]);
  }

  protected cancel(): void {
    void this.router.navigate(['/fee-setup/fee-structures']);
  }

  protected save(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    const v = this.form.getRawValue();
    const details: FeeStructureLineRequest[] = v.lines.map((l) => ({
      feeTypeId: Number(l.feeTypeId),
      frequencyId: Number(l.frequencyId),
      amount: Number(l.amount),
      dueDay: this.isRecurring(l.frequencyId) ? Number(l.dueDay) : null,
      effectiveFrom: l.effectiveFrom,
    }));
    const structureName = v.structureName.trim();

    this.send(structureName, details, v, {});
  }

  private send(structureName: string, details: FeeStructureLineRequest[], v: { academicYearId: string; classId: string }, confirmed: { dueRemoval?: boolean; lineRemoval?: boolean }): void {
    this.saving.set(true);
    const request$ = this.isEdit()
      ? this.service.update(Number(this.feeStructureId()), { structureName, details, ...(confirmed.dueRemoval ? { confirmDueRemoval: true } : {}), ...(confirmed.lineRemoval ? { confirmLineRemoval: true } : {}) })
      : this.service.create({ academicYearId: Number(v.academicYearId), classId: Number(v.classId), structureName, details });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.toast.success(`Fee structure ${this.isEdit() ? 'updated' : 'created'} successfully.`);
        void this.router.navigate(['/fee-setup/fee-structures']);
      },
      error: (e: unknown) => {
        const body = e instanceof HttpErrorResponse ? (e.error as { data?: DueRemovalConfirmation } | null) : null;
        if (e instanceof HttpErrorResponse && e.status === 409 && body?.data?.confirmationRequired) {
          // moving a date later, or removing a fee line, deletes unpaid dues: show how many (and what) and ask
          const c = body.data;
          const removal = c.kind === 'LINE_REMOVAL';
          void this.confirmDialog
            .confirm({
              title: removal ? 'Remove this fee from the structure?' : 'Move the start date later?',
              message: removal ? this.removalMessage(c) : `This removes ${c.duesCount} unpaid due(s) worth ${inr(c.totalAmount)} dated before the new date. Payments already made are never touched.`,
              confirmText: removal ? 'Remove fee and delete dues' : 'Remove and save',
              variant: 'danger',
            })
            .then((ok) => {
              if (ok) this.send(structureName, details, v, { ...confirmed, ...(removal ? { lineRemoval: true } : { dueRemoval: true }) });
            });
          return;
        }
        // the server's reason (409 duplicate class + year, frequency locked, ...) is shown here: the update skips the global toast so the confirmation above can be answered
        if (e instanceof HttpErrorResponse) this.toast.error(extractErrorMessage(e));
      },
    });
  }

  /** The numbers of the dry run, in words: what is deleted (past and future), what is kept, what is switched off. */
  private removalMessage(c: DueRemovalConfirmation): string {
    const lines = c.lines ?? [];
    const per = lines
      .map((l) => `${l.feeTypeName}: ${l.pastCount + l.futureCount} unpaid due(s) worth ${inr(l.pastAmount + l.futureAmount)} (${l.pastCount} past, ${inr(l.pastAmount)}; ${l.futureCount} future, ${inr(l.futureAmount)})`)
      .join('. ');
    const switchedOff = c.itemsDeactivated ?? 0;
    return (
      `${per}. ` +
      (switchedOff > 0 ? `${switchedOff} student fee item(s) already have payments: they are kept but switched off. ` : '') +
      `Dues with a payment are never touched. This is recorded in the removal log.`
    );
  }

  protected openBackdating(index: number): void {
    const line = this.saved(index);
    if (line) this.backdatingLine.set(line);
  }

  protected onBackdatingApplied(): void {
    this.backdatingLine.set(null);
    this.load(); // the line's effective date has moved back
  }
}
