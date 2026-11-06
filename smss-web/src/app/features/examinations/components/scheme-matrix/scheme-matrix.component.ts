import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent } from '../../../../shared/components';
import { EntryMode, SaveCellRequest, Scheme, SchemeExam, SchemeSubject } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

interface Cell { mode: EntryMode; max: string; weight: string }

/**
 * Which exam covers which subject. Tick a cell to examine that subject in that exam. In a marks scheme a cell can also be a GRADE cell: its grade is printed on the card
 * but never summed, ranked or failed (display only). A cell's own maximum and points are optional; blank means the exam's.
 */
@Component({
  selector: 'app-scheme-matrix',
  imports: [ButtonComponent, CardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    @if (scheme().subjects.length === 0 || exams().length === 0) {
      <app-card><p class="note">Add the terms and exams, and the subjects, first. Then tick here which exam covers which subject.</p></app-card>
    } @else {
      <div class="stack">
        @for (c of scheme().classes; track c.classId) {
          <app-card [title]="c.className">
            <div class="table-wrap">
              <table class="dense" [attr.aria-label]="'Subject matrix of ' + c.className">
                <thead>
                  <tr>
                    <th>Subject</th>
                    @for (e of exams(); track e.examId) {
                      <th>
                        <label class="check"><input type="checkbox" [checked]="columnOn(c.classId, e.examId)" [disabled]="!canEdit()" (change)="setColumn(c.classId, e.examId, $any($event.target).checked)" [attr.aria-label]="'All subjects in ' + e.name" /> {{ e.name }}</label>
                      </th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (s of subjectsOf(c.classId); track s.schemeSubjectId) {
                    <tr>
                      <td><label class="check"><input type="checkbox" [checked]="rowOn(s)" [disabled]="!canEdit()" (change)="setRow(s, $any($event.target).checked)" [attr.aria-label]="'All exams for ' + s.subjectName" /> {{ s.labelOverride || s.subjectName }}</label></td>
                      @for (e of exams(); track e.examId) {
                        <td>
                          <label class="check"><input type="checkbox" [checked]="!!cells()[key(e.examId, s.schemeSubjectId)]" [disabled]="!canEdit()" (change)="toggle(e, s, $any($event.target).checked)" [attr.aria-label]="s.subjectName + ' in ' + e.name" /></label>
                          @if (cells()[key(e.examId, s.schemeSubjectId)]; as cell) {
                            <button type="button" class="cell-mode" [attr.aria-label]="'Details of ' + s.subjectName + ' in ' + e.name" (click)="open.set(open() === key(e.examId, s.schemeSubjectId) ? null : key(e.examId, s.schemeSubjectId))">{{ cell.mode === 'GRADE' ? 'Grade' : (cell.max || cell.weight) ? 'Custom' : 'Marks' }}</button>
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </app-card>
        }
        @if (openCell(); as o) {
          <app-card [title]="o.title">
            <div class="grid-2">
              @if (marksScheme()) {
                <label class="field"><span>Entered as</span>
                  <select [disabled]="!canEdit()" (change)="setCell(o.key, 'mode', $any($event.target).value)" aria-label="Entered as">
                    <option value="MARKS" [selected]="o.cell.mode === 'MARKS'">Marks</option>
                    <option value="GRADE" [selected]="o.cell.mode === 'GRADE'">Grade (shown on the card only: never summed, ranked or failed)</option>
                  </select>
                </label>
              }
              @if (o.cell.mode === 'MARKS') {
                <label class="field"><span>Maximum marks</span><input type="number" min="0.01" max="1000" step="0.01" [value]="o.cell.max" [placeholder]="o.exam.defaultMaxMarks" [disabled]="!canEdit()" (input)="setCell(o.key, 'max', $any($event.target).value)" aria-label="Maximum marks for this cell" /><span class="hint">Blank = the exam's {{ o.exam.defaultMaxMarks }}</span></label>
                <label class="field"><span>Points toward the term</span><input type="number" min="0.01" max="1000" step="0.01" [value]="o.cell.weight" [placeholder]="o.exam.weightPoints" [disabled]="!canEdit()" (input)="setCell(o.key, 'weight', $any($event.target).value)" aria-label="Points for this cell" /><span class="hint">Blank = the exam's {{ o.exam.weightPoints }}</span></label>
              }
            </div>
          </app-card>
        }
        @if (canEdit()) { <div class="row-actions"><app-button [disabled]="saving()" (click)="save()">{{ saving() ? 'Saving…' : 'Save subject matrix' }}</app-button></div> }
      </div>
    }
  `,
  styles: `.cell-mode { margin-left: var(--space-2); padding: 0 var(--space-2); border: 1px solid var(--color-border); border-radius: 999px; background: var(--color-surface, #fff); font: inherit; font-size: var(--text-xs); cursor: pointer; }`,
})
export class SchemeMatrixComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly toast = inject(ToastService);

  readonly scheme = input.required<Scheme>();
  readonly canEdit = input(false);
  readonly saved = output<Scheme>();

  protected readonly cells = signal<Record<string, Cell>>({});
  protected readonly open = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly exams = computed<SchemeExam[]>(() => this.scheme().terms.flatMap((t) => t.exams));
  protected readonly marksScheme = computed(() => this.scheme().evaluationMode === 'MARKS');

  protected readonly openCell = computed(() => {
    const k = this.open();
    if (!k) return null;
    const cell = this.cells()[k];
    if (!cell) return null;
    const [examId, ssId] = k.split('|').map(Number);
    const exam = this.exams().find((e) => e.examId === examId);
    const subject = this.scheme().subjects.find((s) => s.schemeSubjectId === ssId);
    return exam && subject ? { key: k, cell, exam, title: `${subject.className} · ${subject.labelOverride || subject.subjectName} in ${exam.name}` } : null;
  });

  ngOnInit(): void {
    const next: Record<string, Cell> = {};
    for (const c of this.scheme().cells) next[this.key(c.examId, c.schemeSubjectId)] = { mode: c.entryMode, max: c.maxMarks == null ? '' : String(c.maxMarks), weight: c.weightPoints == null ? '' : String(c.weightPoints) };
    this.cells.set(next);
  }

  protected key(examId: number, ssId: number): string { return `${examId}|${ssId}`; }
  protected subjectsOf(classId: number): SchemeSubject[] { return this.scheme().subjects.filter((s) => s.classId === classId); }
  private freshMode(): EntryMode { return this.marksScheme() ? 'MARKS' : 'GRADE'; }

  protected toggle(e: SchemeExam, s: SchemeSubject, on: boolean): void {
    this.cells.update((c) => { const n = { ...c }; const k = this.key(e.examId, s.schemeSubjectId); if (on) n[k] = { mode: this.freshMode(), max: '', weight: '' }; else delete n[k]; return n; });
    if (!on && this.open() === this.key(e.examId, s.schemeSubjectId)) this.open.set(null);
  }

  protected rowOn(s: SchemeSubject): boolean { return this.exams().length > 0 && this.exams().every((e) => !!this.cells()[this.key(e.examId, s.schemeSubjectId)]); }
  protected columnOn(classId: number, examId: number): boolean { const ss = this.subjectsOf(classId); return ss.length > 0 && ss.every((s) => !!this.cells()[this.key(examId, s.schemeSubjectId)]); }

  protected setRow(s: SchemeSubject, on: boolean): void { for (const e of this.exams()) if (!!this.cells()[this.key(e.examId, s.schemeSubjectId)] !== on) this.toggle(e, s, on); }
  protected setColumn(classId: number, examId: number, on: boolean): void {
    const e = this.exams().find((x) => x.examId === examId)!;
    for (const s of this.subjectsOf(classId)) if (!!this.cells()[this.key(examId, s.schemeSubjectId)] !== on) this.toggle(e, s, on);
  }

  protected setCell(k: string, f: keyof Cell, v: string): void {
    this.cells.update((c) => ({ ...c, [k]: { ...c[k], [f]: v, ...(f === 'mode' && v === 'GRADE' ? { max: '', weight: '' } : {}) } }));
  }

  protected save(): void {
    if (this.saving()) return;
    const body: SaveCellRequest[] = Object.entries(this.cells()).map(([k, c]) => {
      const [examId, schemeSubjectId] = k.split('|').map(Number);
      return { examId, schemeSubjectId, entryMode: c.mode, maxMarks: c.mode === 'MARKS' && c.max !== '' ? Number(c.max) : null, weightPoints: c.mode === 'MARKS' && c.weight !== '' ? Number(c.weight) : null };
    });
    this.saving.set(true);
    this.service.saveMatrix(this.scheme().schemeId, body).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (s) => { this.toast.success('Subject matrix saved.'); this.saved.emit(s); },
      error: () => undefined,
    });
  }
}
