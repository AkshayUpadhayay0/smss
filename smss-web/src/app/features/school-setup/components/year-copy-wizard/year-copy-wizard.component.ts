import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { AcademicYear } from '../../models/academic-year.model';
import { CopyPart, YearCopyResult } from '../../models/academic-setup.model';
import { AcademicSetupService } from '../../services/academic-setup.service';

export interface PartChoice { part: CopyPart; label: string; hint: string }

/** In dependency order: a later part only hangs on what the target already has (or what an earlier part just added). */
export const PART_CHOICES: PartChoice[] = [
  { part: 'CLASSES_SECTIONS', label: 'Classes and sections', hint: 'Which classes and sections run this year, with their capacity.' },
  { part: 'FEE_STRUCTURES', label: 'Fee structures', hint: 'The fee structure of each class; every line starts on the first day of the year. Classes not offered this year are left out.' },
  { part: 'SUBJECTS', label: 'Subjects', hint: 'The subjects of each class, and the sections that differ.' },
  { part: 'SCHEDULES', label: 'Bell schedule assignments', hint: 'Which schedule each class follows on each day.' },
  { part: 'CLASS_TEACHERS', label: 'Class teachers', hint: 'Only teachers who are still active; they start on the first day of the year.' },
  { part: 'TIMETABLE', label: 'Timetables', hint: 'Copied as drafts, never activated. A teacher who is no longer active leaves the slot open.' },
];

/** The year the copy most likely comes from: the latest one that starts before the target. */
export function defaultSource(target: AcademicYear, years: AcademicYear[]): AcademicYear | undefined {
  return years.filter((y) => y.academicYearId !== target.academicYearId && y.startDate < target.startDate).sort((a, b) => b.startDate.localeCompare(a.startDate))[0]
    ?? years.find((y) => y.academicYearId !== target.academicYearId);
}

/** "12 rows" / "nothing new". */
export const rowsLabel = (n: number): string => (n === 0 ? 'nothing new' : `${n} row${n === 1 ? '' : 's'}`);

type Step = 'choose' | 'preview' | 'done';

/**
 * Copy configuration from another academic year into this one (S7): choose the source and the parts, see a PREVIEW (nothing is saved), then confirm.
 * It only ADDS: anything the year already has is left as it is, nothing is overwritten or deleted, the source year is never touched, and students, fees and
 * attendance are never copied. Running it again adds nothing new.
 */
@Component({
  selector: 'app-year-copy-wizard',
  imports: [ButtonComponent, IconComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './year-copy-wizard.component.scss',
  templateUrl: './year-copy-wizard.component.html',
})
export class YearCopyWizardComponent {
  private readonly api = inject(AcademicSetupService);
  private readonly toast = inject(ToastService);

  readonly target = input.required<AcademicYear>();
  readonly years = input.required<AcademicYear[]>();
  readonly closed = output<void>();
  /** The copy ran (the lists behind may have changed). */
  readonly done = output<YearCopyResult>();

  protected readonly step = signal<Step>('choose');
  protected readonly busy = signal(false);
  protected readonly sourceId = signal<string>('');
  protected readonly picked = signal<CopyPart[]>(PART_CHOICES.map((c) => c.part).filter((p) => p !== 'FEE_STRUCTURES'));
  /** Whole percent added to every copied fee amount (0 = same amounts). */
  protected readonly percent = signal(0);
  protected readonly percentValid = computed(() => Number.isFinite(this.percent()) && this.percent() >= 0 && this.percent() <= 100);
  protected readonly result = signal<YearCopyResult | null>(null);
  protected readonly choices = PART_CHOICES;
  protected readonly rows = rowsLabel;

  protected readonly sources = computed(() => this.years().filter((y) => y.academicYearId !== this.target().academicYearId));
  protected readonly effectiveSource = computed(() => this.sourceId() || String(defaultSource(this.target(), this.years())?.academicYearId ?? ''));
  protected readonly sourceName = computed(() => this.sources().find((y) => String(y.academicYearId) === this.effectiveSource())?.yearName ?? '');
  protected readonly willCreate = computed(() => this.result()?.parts.reduce((n, p) => n + p.created, 0) ?? 0);
  protected readonly canPreview = computed(() => !this.busy() && this.effectiveSource() !== '' && this.picked().length > 0 && this.percentValid());

  protected toggle(part: CopyPart, on: boolean): void {
    this.picked.update((l) => (on ? [...l.filter((p) => p !== part), part] : l.filter((p) => p !== part)));
  }

  private feePercent(): number {
    return this.has('FEE_STRUCTURES') ? this.percent() : 0;
  }

  protected setPercent(v: string): void {
    this.percent.set(v.trim() === '' ? 0 : Number(v));
  }

  protected has(part: CopyPart): boolean {
    return this.picked().includes(part);
  }

  protected runPreview(): void {
    if (!this.canPreview()) return;
    this.busy.set(true);
    this.api.previewCopy(this.target().academicYearId, Number(this.effectiveSource()), this.picked(), this.feePercent()).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (r) => { this.result.set(r); this.step.set('preview'); },
      error: () => undefined,   // e.g. "the target year is closed": already toasted
    });
  }

  protected back(): void {
    this.step.set('choose');
    this.result.set(null);
  }

  protected run(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.applyCopy(this.target().academicYearId, Number(this.effectiveSource()), this.picked(), this.feePercent()).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (r) => {
        this.result.set(r);
        this.step.set('done');
        this.toast.success(r.parts.reduce((n, p) => n + p.created, 0) === 0 ? 'Nothing new to copy.' : `Configuration copied into ${r.targetYearName}.`);
        this.done.emit(r);
      },
      error: () => undefined,
    });
  }
}
