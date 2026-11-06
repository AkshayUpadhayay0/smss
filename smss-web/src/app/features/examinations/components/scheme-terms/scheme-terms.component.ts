import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent } from '../../../../shared/components';
import { SaveTermRequest, Scheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

interface ExamRow { examId: number | null; name: string; max: string; weight: string }
interface TermRow { termId: number | null; name: string; start: string; end: string; weight: string; exams: ExamRow[] }

/**
 * Terms and their exams. Terms are in date order and cannot overlap (attendance is counted between a term's dates). Each exam has a maximum and the points it adds to the
 * term total; leave the points blank for "the same as the maximum" (10 + 10 + 80 gives a term of 100).
 */
@Component({
  selector: 'app-scheme-terms',
  imports: [ButtonComponent, CardComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <div class="stack">
      @for (t of terms(); track $index; let ti = $index) {
        <app-card>
          <div class="bar">
            <strong>Term {{ ti + 1 }}</strong>
            @if (canEdit()) {
              <div class="row-actions">
                <app-button variant="secondary" size="sm" aria-label="Remove term" [disabled]="terms().length === 1" (click)="removeTerm(ti)"><app-icon name="trash-2" [size]="14" /></app-button>
              </div>
            }
          </div>
          <div class="grid-2">
            <label class="field"><span>Name</span><input type="text" maxlength="60" [value]="t.name" [disabled]="!canEdit()" (input)="setTerm(ti, 'name', $any($event.target).value)" [attr.aria-label]="'Term ' + (ti + 1) + ' name'" /></label>
            <label class="field"><span>From</span><input type="date" [value]="t.start" [disabled]="!canEdit()" (change)="setTerm(ti, 'start', $any($event.target).value)" [attr.aria-label]="'Term ' + (ti + 1) + ' from'" /></label>
            <label class="field"><span>To</span><input type="date" [value]="t.end" [disabled]="!canEdit()" (change)="setTerm(ti, 'end', $any($event.target).value)" [attr.aria-label]="'Term ' + (ti + 1) + ' to'" /></label>
            @if (weighted()) {
              <label class="field"><span>Weight in the year</span><input type="number" min="0.01" [value]="t.weight" [disabled]="!canEdit()" (input)="setTerm(ti, 'weight', $any($event.target).value)" [attr.aria-label]="'Term ' + (ti + 1) + ' weight'" /></label>
            }
          </div>
          <div class="table-wrap">
            <table class="dense" [attr.aria-label]="'Exams of ' + t.name">
              <thead><tr><th>Exam</th><th>Max marks</th><th>Points toward the term</th><th></th></tr></thead>
              <tbody>
                @for (e of t.exams; track $index; let ei = $index) {
                  <tr>
                    <td><input type="text" maxlength="60" [value]="e.name" [disabled]="!canEdit()" (input)="setExam(ti, ei, 'name', $any($event.target).value)" [attr.aria-label]="'Exam ' + (ei + 1) + ' name'" /></td>
                    <td><input type="number" min="0.01" max="1000" step="0.01" [value]="e.max" [disabled]="!canEdit()" (input)="setExam(ti, ei, 'max', $any($event.target).value)" [attr.aria-label]="'Exam ' + (ei + 1) + ' max marks'" /></td>
                    <td><input type="number" min="0.01" max="1000" step="0.01" [value]="e.weight" [placeholder]="e.max" [disabled]="!canEdit()" (input)="setExam(ti, ei, 'weight', $any($event.target).value)" [attr.aria-label]="'Exam ' + (ei + 1) + ' points'" /></td>
                    <td>
                      <div class="row-actions">
                        @if (canEdit()) {
                          <app-button variant="secondary" size="sm" aria-label="Move up" [disabled]="ei === 0" (click)="moveExam(ti, ei, -1)"><app-icon name="chevron-down" [size]="14" style="transform: rotate(180deg)" /></app-button>
                          <app-button variant="secondary" size="sm" aria-label="Move down" [disabled]="ei === t.exams.length - 1" (click)="moveExam(ti, ei, 1)"><app-icon name="chevron-down" [size]="14" /></app-button>
                          <app-button variant="danger-ghost" size="sm" aria-label="Remove exam" [disabled]="t.exams.length === 1" (click)="removeExam(ti, ei)"><app-icon name="trash-2" [size]="14" /></app-button>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
              <tfoot><tr><td colspan="2"><strong>Term total</strong></td><td colspan="2"><strong>{{ total(t) }}</strong> points per subject</td></tr></tfoot>
            </table>
          </div>
          @if (canEdit()) { <div style="margin-top: var(--space-3)"><app-button variant="secondary" size="sm" (click)="addExam(ti)"><app-icon name="plus" [size]="14" />Add an exam</app-button></div> }
        </app-card>
      }
      @if (canEdit()) {
        <div class="row-actions" style="justify-content: space-between">
          <app-button variant="secondary" [disabled]="terms().length >= 6" (click)="addTerm()"><app-icon name="plus" [size]="16" />Add a term</app-button>
          <app-button [disabled]="saving() || !!problem()" (click)="save()">{{ saving() ? 'Saving…' : 'Save terms and exams' }}</app-button>
        </div>
        @if (problem(); as p) { <p class="error-text" role="alert">{{ p }}</p> }
      }
    </div>
  `,
})
export class SchemeTermsComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  readonly scheme = input.required<Scheme>();
  readonly canEdit = input(false);
  readonly saved = output<Scheme>();

  protected readonly terms = signal<TermRow[]>([]);
  protected readonly saving = signal(false);
  protected readonly weighted = computed(() => this.scheme().yearCombine === 'WEIGHTED');

  protected readonly problem = computed<string | null>(() => {
    const ts = this.terms();
    for (const t of ts) {
      if (!t.name.trim()) return 'Every term needs a name.';
      if (!t.start || !t.end) return `${t.name}: choose the dates.`;
      if (t.end < t.start) return `${t.name}: the end date is before the start date.`;
      if (this.weighted() && !(Number(t.weight) > 0)) return `${t.name}: give the term a weight.`;
      if (t.exams.some((e) => !e.name.trim() || !(Number(e.max) > 0))) return `${t.name}: every exam needs a name and maximum marks.`;
    }
    for (let i = 1; i < ts.length; i++) if (ts[i].start <= ts[i - 1].end) return `${ts[i].name} starts before ${ts[i - 1].name} ends. Terms can't overlap and must be in date order.`;
    return null;
  });

  ngOnInit(): void {
    this.terms.set(this.scheme().terms.map((t) => ({
      termId: t.termId, name: t.name, start: t.startDate, end: t.endDate, weight: t.yearWeight == null ? '' : String(t.yearWeight),
      exams: t.exams.map((e) => ({ examId: e.examId, name: e.name, max: String(e.defaultMaxMarks), weight: e.weightPoints === e.defaultMaxMarks ? '' : String(e.weightPoints) })),
    })));
    if (this.terms().length === 0 && this.canEdit()) this.addTerm();
  }

  protected total(t: TermRow): number { return Math.round(t.exams.reduce((a, e) => a + (Number(e.weight || e.max) || 0), 0) * 100) / 100; }

  protected setTerm(i: number, f: 'name' | 'start' | 'end' | 'weight', v: string): void { this.terms.update((ts) => ts.map((t, j) => (j === i ? { ...t, [f]: v } : t))); }
  protected setExam(ti: number, ei: number, f: 'name' | 'max' | 'weight', v: string): void {
    this.terms.update((ts) => ts.map((t, j) => (j === ti ? { ...t, exams: t.exams.map((e, k) => (k === ei ? { ...e, [f]: v } : e)) } : t)));
  }
  protected addTerm(): void {
    const last = this.terms().at(-1);
    this.terms.update((ts) => [...ts, { termId: null, name: `Term ${ts.length + 1}`, start: '', end: '', weight: '', exams: [{ examId: null, name: '', max: '100', weight: '' }] }]);
    void last;
  }
  protected removeTerm(i: number): void { this.terms.update((ts) => ts.filter((_, j) => j !== i)); }
  protected addExam(ti: number): void { this.terms.update((ts) => ts.map((t, j) => (j === ti ? { ...t, exams: [...t.exams, { examId: null, name: '', max: '10', weight: '' }] } : t))); }
  protected removeExam(ti: number, ei: number): void { this.terms.update((ts) => ts.map((t, j) => (j === ti ? { ...t, exams: t.exams.filter((_, k) => k !== ei) } : t))); }
  protected moveExam(ti: number, ei: number, d: number): void {
    this.terms.update((ts) => ts.map((t, j) => {
      if (j !== ti) return t;
      const exams = [...t.exams];
      [exams[ei], exams[ei + d]] = [exams[ei + d], exams[ei]];
      return { ...t, exams };
    }));
  }

  protected async save(): Promise<void> {
    if (this.saving() || this.problem()) return;
    const keptTerms = new Set(this.terms().map((t) => t.termId).filter((x) => x != null));
    const keptExams = new Set(this.terms().flatMap((t) => t.exams.map((e) => e.examId)).filter((x) => x != null));
    const dropped = this.scheme().terms.filter((t) => !keptTerms.has(t.termId)).length + this.scheme().terms.flatMap((t) => t.exams).filter((e) => !keptExams.has(e.examId)).length;
    if (dropped > 0 && !(await this.confirm.confirm({ title: 'Remove terms or exams?', message: 'You removed a term or an exam: its subject matrix goes with it.', confirmText: 'Save anyway', variant: 'danger' }))) return;
    const body: SaveTermRequest[] = this.terms().map((t) => ({
      termId: t.termId, name: t.name.trim(), startDate: t.start, endDate: t.end, yearWeight: this.weighted() ? Number(t.weight) : null,
      exams: t.exams.map((e) => ({ examId: e.examId, name: e.name.trim(), defaultMaxMarks: Number(e.max), weightPoints: e.weight === '' ? null : Number(e.weight) })),
    }));
    this.saving.set(true);
    this.service.saveTerms(this.scheme().schemeId, body).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (s) => { this.toast.success('Terms and exams saved.'); this.saved.emit(s); },
      error: () => undefined,
    });
  }
}
