import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { SchoolClass } from '../../../school-setup/models/class.model';
import { Section } from '../../../school-setup/models/section.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { ClassService } from '../../../school-setup/services/class.service';
import { SectionService } from '../../../school-setup/services/section.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { DECISION_LABELS, PromotionDecision, PromotionDecisionInput, PromotionPreview, PromotionRow, PromotionRowStatus, STATUS_LABELS } from '../../models/promotion.model';
import { PromotionService } from '../../services/promotion.service';

/** The year a promotion most likely starts from (the current one) and goes to (the first later year). */
export function defaultYears(years: AcademicYear[]): { source: number | null; target: number | null } {
  const sorted = [...years].sort((a, b) => a.startDate.localeCompare(b.startDate));
  const source = sorted.find((y) => y.isCurrent) ?? sorted[sorted.length - 1];
  const target = source ? sorted.find((y) => y.startDate > source.startDate && !y.isClosed) : undefined;
  return { source: source?.academicYearId ?? null, target: target?.academicYearId ?? null };
}

/**
 * Promotion, step one (4b-2): PREVIEW only. Choose the years, a class (and optionally one section) and the promotion date; the screen shows what a run would do for each student, with the class
 * teacher's decision prefilled, the next class and section, roll numbers, capacity, the dues block (with the per-due list) and warnings. Every edit asks the server again, so the numbers are always the
 * server's. Nothing is written from this screen; the run itself comes in the next step.
 */
@Component({
  selector: 'app-promotion',
  imports: [CurrencyPipe, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './promotion.component.scss',
  templateUrl: './promotion.component.html',
})
export class PromotionComponent implements OnInit {
  private readonly api = inject(PromotionService);
  private readonly yearApi = inject(AcademicYearService);
  private readonly classApi = inject(ClassService);
  private readonly sectionApi = inject(SectionService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly decisions: PromotionDecision[] = ['PROMOTE', 'REPEAT', 'PASS_OUT', 'LEAVE'];
  protected readonly decisionLabels = DECISION_LABELS;
  protected readonly statusLabels = STATUS_LABELS;

  protected readonly years = signal<AcademicYear[]>([]);
  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly sections = signal<Section[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly previewFailed = signal(false);

  protected readonly sourceId = signal<number | null>(null);
  protected readonly targetId = signal<number | null>(null);
  protected readonly classId = signal<number | null>(null);
  protected readonly sectionId = signal<number | null>(null);
  protected readonly date = signal('');
  protected readonly edits = signal<Record<string, PromotionDecisionInput>>({});
  protected readonly result = signal<PromotionPreview | null>(null);
  protected readonly expanded = signal<Record<string, boolean>>({});

  protected readonly classSections = computed(() => this.sections().filter((s) => s.classId === this.classId()));
  protected readonly canPreview = computed(() => !this.busy() && this.sourceId() != null && this.targetId() != null && this.classId() != null && this.sourceId() !== this.targetId());

  ngOnInit(): void {
    forkJoin({ years: this.yearApi.list(), classes: this.classApi.list(), sections: this.sectionApi.list() })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ years, classes, sections }) => {
          this.years.set(years);
          this.classes.set([...classes].sort((a, b) => a.sequenceOrder - b.sequenceOrder));
          this.sections.set(sections);
          const d = defaultYears(years);
          this.sourceId.set(d.source);
          this.targetId.set(d.target);
        },
        error: () => this.loadFailed.set(true),
      });
  }

  protected num(v: string): number | null {
    return v === '' ? null : Number(v);
  }

  protected pickClass(v: string): void {
    this.classId.set(this.num(v));
    this.sectionId.set(null);
    this.reset();
  }

  /** A change of years, class, section or date starts the preview over (the old edits belonged to the old choice). */
  protected reset(): void {
    this.edits.set({});
    this.result.set(null);
    this.previewFailed.set(false);
  }

  protected runPreview(): void {
    if (!this.canPreview()) return;
    this.busy.set(true);
    this.previewFailed.set(false);
    this.api
      .preview({
        sourceYearId: this.sourceId()!, targetYearId: this.targetId()!, classId: this.classId()!, sectionId: this.sectionId(),
        promotionDate: this.date() || null, decisions: Object.values(this.edits()),
      })
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (r) => this.result.set(r), error: () => this.previewFailed.set(true) });
  }

  /** Records what the admin changed on one row and asks the server again. */
  protected edit(row: PromotionRow, patch: Partial<PromotionDecisionInput>): void {
    const current = this.edits()[row.studentId] ?? { studentId: row.studentId, decision: row.decision };
    this.edits.update((e) => ({ ...e, [row.studentId]: { ...current, ...patch } }));
    this.runPreview();
  }

  protected pickDecision(row: PromotionRow, v: string): void {
    // switching the decision drops a class/section/roll chosen for the old one
    this.edit(row, { decision: (v || null) as PromotionDecision | null, toClassId: null, toSectionId: null, rollNumber: null });
  }

  protected pickTargetClass(row: PromotionRow, v: string): void {
    this.edit(row, { toClassId: this.num(v), toSectionId: null, rollNumber: null });
  }

  protected pickTargetSection(row: PromotionRow, v: string): void {
    this.edit(row, { toClassId: row.target?.classId ?? null, toSectionId: this.num(v), rollNumber: null });
  }

  protected typeRoll(row: PromotionRow, v: string): void {
    if ((row.rollNumber ?? '') === v.trim()) return;
    this.edit(row, { toClassId: row.target?.classId ?? null, toSectionId: row.target?.sectionId ?? null, rollNumber: v.trim() || null });
  }

  protected sectionsOf(classId: number | null | undefined) {
    return this.result()?.targetOptions.find((c) => c.classId === classId)?.sections ?? [];
  }

  protected toggle(studentId: string): void {
    this.expanded.update((e) => ({ ...e, [studentId]: !e[studentId] }));
  }

  protected badge(s: PromotionRowStatus): string {
    return s === 'READY' ? 'ok' : s === 'BLOCKED' || s === 'NEEDS_ATTENTION' ? 'bad' : 'muted';
  }

  /** A row the decision select applies to: skipped rows (already enrolled / recorded) cannot be edited. */
  protected editable(row: PromotionRow): boolean {
    return row.status !== 'ALREADY_ENROLLED' && row.status !== 'ALREADY_RECORDED';
  }
}
