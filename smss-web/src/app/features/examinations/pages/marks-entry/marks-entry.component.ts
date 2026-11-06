import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Observable, finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { saveBlob } from '../../../homework/utils/homework-format';
import { CellEdit, EntryState, GridCell, GridStudent, MarksContext, MarksContextRow, MarksGrid, STATUS_LABEL, UploadPreview, cellKey, distinct, editsToEntries, enteredCount } from '../../models/marks.model';
import { MarksService } from '../../services/marks.service';

/**
 * Marks entry: pick the exam, the section and the subject, then fill a student-by-skill grid. Tab moves across, Enter moves down. Absent and Exempt are toggles.
 * Nothing is saved until Save draft; a stale save (someone else changed the grid) is refused by the server and shown as a banner, and what you typed is kept.
 */
@Component({
  selector: 'app-marks-entry',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="Marks Entry" subtitle="Enter, submit and review marks for one exam subject at a time" icon="pencil" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading what you can enter…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">Marks entry could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (rows().length === 0) {
      <app-card><p class="note pad">There is nothing for you to enter marks for yet. Marks are entered by the School Admin, the section's class teacher, or the teacher of the subject in that section, once a report scheme with exams has been set up.</p></app-card>
    } @else {
      <app-card>
        <div class="grid-2">
          @if (schemes().length > 1) {
            <label class="field"><span>Report scheme</span>
              <select [value]="schemeId()" (change)="pick('scheme', $any($event.target))" aria-label="Report scheme">
                @for (s of schemes(); track s.schemeId) { <option [value]="s.schemeId" [selected]="s.schemeId === schemeId()">{{ s.schemeName }}</option> }
              </select></label>
          }
          <label class="field"><span>Exam</span>
            <select [value]="examId()" (change)="pick('exam', $any($event.target))" aria-label="Exam">
              @for (e of exams(); track e.examId) { <option [value]="e.examId" [selected]="e.examId === examId()">{{ e.termName }} · {{ e.examName }}</option> }
            </select></label>
          <label class="field"><span>Section</span>
            <select [value]="sectionId()" (change)="pick('section', $any($event.target))" aria-label="Section">
              @for (s of sections(); track s.sectionId) { <option [value]="s.sectionId" [selected]="s.sectionId === sectionId()">{{ s.className }} {{ s.sectionName }}</option> }
            </select></label>
          <label class="field"><span>Subject</span>
            <select [value]="examSubjectId()" (change)="pick('subject', $any($event.target))" aria-label="Subject">
              @for (s of subjects(); track s.examSubjectId) { <option [value]="s.examSubjectId" [selected]="s.examSubjectId === examSubjectId()">{{ s.subjectName }} ({{ statusText(s.status, s.locked) }})</option> }
            </select></label>
        </div>
      </app-card>

      @if (gridLoading()) {
        <app-card><p class="note" role="status">Loading the grid…</p></app-card>
      } @else if (grid(); as g) {
        <app-card [title]="g.subjectName + ' · ' + g.examName + ' · ' + g.className + ' ' + g.sectionName">
          <div class="bar">
            <div>
              <span class="pill" [class.green]="g.status === 'ACCEPTED'" [class.blue]="g.status === 'SUBMITTED'">{{ g.locked ? 'Locked' : statusText(g.status, false) }}</span>
              <span class="note"> <strong>{{ progress().done }} of {{ progress().total }}</strong> entered@if (g.entryMode === 'MARKS') { · out of {{ g.maxMarks }} }@if (dirtyCount() > 0) { · <strong>{{ dirtyCount() }} unsaved</strong> }</span>
            </div>
            <div class="row-actions">
              <app-button variant="secondary" size="sm" [disabled]="busy()" (click)="downloadTemplate()"><app-icon name="download" [size]="14" />Excel template</app-button>
              @if (g.canEdit) {
                <label class="ctl-file"><input type="file" accept=".xlsx" (change)="onFile($event)" aria-label="Upload a filled Excel sheet" /><span><app-icon name="upload" [size]="14" /> Upload Excel</span></label>
              }
            </div>
          </div>

          @if (g.readOnlyReason) { <p class="note" role="status">{{ g.readOnlyReason }}</p> }
          @if (stale()) {
            <p class="error-text" role="alert">Someone else changed this grid after you opened it. <app-button variant="secondary" size="sm" (click)="reload(true)">Reload the latest (keeps what you typed)</app-button></p>
          }
          @if (problems().length > 0) { <ul class="warnings" role="alert">@for (p of problems().slice(0, 4); track p) { <li>{{ p }}</li> }</ul> }

          @if (upload(); as u) {
            <div class="warnings" role="status">
              <strong>Excel file: {{ u.cells }} cells from {{ u.rows }} rows.</strong>
              @if (u.issues.length > 0) {
                <ul>@for (i of u.issues.slice(0, 8); track i.message + i.row) { <li>{{ i.row ? 'Row ' + i.row + ': ' : '' }}{{ i.message }}</li> }</ul>
                @if (u.issues.length > 8) { <p class="note">…and {{ u.issues.length - 8 }} more. Fix the file and upload it again.</p> }
              } @else { <p class="note">No problems. Fill the grid with it, then check it and Save draft.</p> }
              <div class="row-actions"><app-button size="sm" [disabled]="u.issues.length > 0" (click)="applyUpload(u)">Fill the grid</app-button><app-button variant="secondary" size="sm" (click)="upload.set(null)">Discard</app-button></div>
            </div>
          }

          <div class="table-wrap">
            <table class="dense" [attr.aria-label]="'Marks for ' + g.subjectName">
              <thead>
                <tr>
                  <th>Roll</th><th>Student</th>
                  @if (g.skills.length > 0) { @for (k of g.skills; track k.skillId) { <th>{{ k.name }}</th> } }
                  @else { <th>{{ g.entryMode === 'MARKS' ? 'Marks' : 'Grade' }}</th> }
                </tr>
              </thead>
              <tbody>
                @for (s of g.students; track s.studentId; let r = $index) {
                  <tr [class.left]="!s.onRoster">
                    <td>{{ s.rollNumber || '—' }}</td>
                    <td>{{ s.fullName }}@if (!s.onRoster) { <small class="note"> (left the section)</small> }</td>
                    @for (c of s.cells; track c.skillId ?? 0; let col = $index) {
                      <td>
                        @if (g.entryMode === 'MARKS') {
                          <div class="cellbox"><input type="text" inputmode="decimal" [attr.data-cell]="r + '-' + col" [value]="markText(s, c)" [disabled]="!g.canEdit || !s.onRoster || view(s, c).state === 'ABSENT' || view(s, c).state === 'EXEMPT'"
                                 [placeholder]="view(s, c).state === 'ABSENT' ? 'Absent' : view(s, c).state === 'EXEMPT' ? 'Exempt' : ''" [class.bad]="isBad(s, c)" (input)="typeMarks(s, c, $any($event.target).value)" (keydown.enter)="down($event, r, col)" [attr.aria-label]="s.fullName + ' marks'" />
                            <button type="button" class="tog" [attr.aria-pressed]="view(s, c).state === 'ABSENT'" [disabled]="!g.canEdit || !s.onRoster" (click)="toggle(s, c, 'ABSENT')" [attr.aria-label]="s.fullName + ' absent'">Ab</button>
                            <button type="button" class="tog" [attr.aria-pressed]="view(s, c).state === 'EXEMPT'" [disabled]="!g.canEdit || !s.onRoster" (click)="toggle(s, c, 'EXEMPT')" [attr.aria-label]="s.fullName + ' exempt'">Ex</button></div>
                        } @else {
                          <select [attr.data-cell]="r + '-' + col" [value]="gradeText(s, c)" [disabled]="!g.canEdit || !s.onRoster" (change)="pickGrade(s, c, $any($event.target).value)" (keydown.enter)="down($event, r, col)" [attr.aria-label]="s.fullName + (c.skillId ? ' ' + skillName(c.skillId) : ' grade')">
                            <option value="" [selected]="gradeText(s, c) === ''">—</option>
                            @for (o of g.gradeOptions; track o.code) { <option [value]="o.code" [selected]="gradeText(s, c) === o.code">{{ o.code }}{{ o.label && o.label !== o.code ? ' · ' + o.label : '' }}</option> }
                            <option value="AB" [selected]="gradeText(s, c) === 'AB'">Absent</option><option value="EX" [selected]="gradeText(s, c) === 'EX'">Exempt</option>
                          </select>
                        }
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="row-actions" style="margin-top: var(--space-4)">
            @if (g.canEdit) {
              <app-button variant="secondary" [disabled]="busy() || dirtyCount() === 0 || problems().length > 0" (click)="saveDraft()">{{ busy() ? 'Saving…' : 'Save draft' }}</app-button>
              <app-button [disabled]="busy() || problems().length > 0 || progress().done < progress().total" (click)="submit()">Submit for review</app-button>
            }
            @if (g.canAccept) { <app-button [disabled]="busy()" (click)="accept()">Accept</app-button> }
            @if (g.canReopen) { <app-button variant="secondary" [disabled]="busy()" (click)="reopening.set(true)">Reopen…</app-button> }
          </div>
          @if (g.canEdit && progress().done < progress().total) { <p class="note">Submit becomes available when every student has marks, Absent or Exempt.</p> }

          @if (reopening()) {
            <div class="warnings" role="group" aria-label="Reopen">
              <label class="field"><span>Why are you reopening it? (kept in the audit trail)</span><input type="text" maxlength="300" [value]="reason()" (input)="reason.set($any($event.target).value)" /></label>
              <div class="row-actions"><app-button size="sm" [disabled]="reason().trim().length < 3 || busy()" (click)="reopen()">Reopen</app-button><app-button variant="secondary" size="sm" (click)="reopening.set(false)">Cancel</app-button></div>
            </div>
          }
        </app-card>
      }
    }
  `,
  styles: `
    .cellbox { display: flex; align-items: center; gap: 4px; }
    .cellbox input { width: 72px; }
    .tog { min-width: 34px; height: 30px; border: 1px solid var(--color-border); border-radius: var(--radius-md); background: var(--color-surface, #fff); font: inherit; cursor: pointer; }
    .tog[aria-pressed='true'] { background: var(--color-primary); border-color: var(--color-primary); color: #fff; }
    .tog:disabled { opacity: 0.5; cursor: default; }
    input.bad { border-color: var(--color-danger); }
    tr.left td { color: var(--color-text-muted); background: var(--color-surface-muted, #f4f4f8); }
    .ctl-file { display: inline-flex; align-items: center; gap: 6px; height: 32px; padding: 0 var(--space-3); border: 1px solid var(--color-border); border-radius: var(--radius-md); cursor: pointer; font-size: var(--text-sm); }
    .ctl-file input { position: absolute; width: 1px; height: 1px; opacity: 0; }
    .ctl-file:focus-within { outline: 2px solid var(--color-primary); }
  `,
})
export class MarksEntryComponent implements OnInit {
  private readonly service = inject(MarksService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Marks Entry' }];
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly gridLoading = signal(false);
  protected readonly busy = signal(false);
  protected readonly stale = signal(false);
  protected readonly reopening = signal(false);
  protected readonly reason = signal('');
  protected readonly ctx = signal<MarksContext | null>(null);
  protected readonly schemeId = signal(0);
  protected readonly examId = signal(0);
  protected readonly sectionId = signal(0);
  protected readonly examSubjectId = signal(0);
  protected readonly grid = signal<MarksGrid | null>(null);
  protected readonly edits = signal<Record<string, CellEdit>>({});
  protected readonly upload = signal<UploadPreview | null>(null);

  protected readonly rows = computed(() => this.ctx()?.rows ?? []);
  protected readonly schemes = computed(() => distinct(this.rows(), (r) => r.schemeId));
  private readonly inScheme = computed(() => this.rows().filter((r) => r.schemeId === this.schemeId()));
  protected readonly exams = computed(() => distinct(this.inScheme(), (r) => r.examId));
  private readonly inExam = computed(() => this.inScheme().filter((r) => r.examId === this.examId()));
  protected readonly sections = computed(() => distinct(this.inExam(), (r) => r.sectionId));
  protected readonly subjects = computed(() => this.inExam().filter((r) => r.sectionId === this.sectionId()));
  protected readonly progress = computed(() => { const g = this.grid(); return g ? enteredCount(g, this.edits()) : { done: 0, total: 0 }; });
  protected readonly dirtyCount = computed(() => Object.keys(this.edits()).length);
  /** Client-side checks, the same rules the server applies: shown while typing so a mistake is caught before Save. */
  protected readonly problems = computed(() => {
    const g = this.grid(); if (!g) return [] as string[];
    const out: string[] = [];
    for (const s of g.students) for (const c of s.cells) if (this.isBad(s, c)) out.push(`${s.fullName}: marks must be between 0 and ${g.maxMarks}, with at most two decimals.`);
    return out;
  });

  protected statusText(status: string, locked: boolean): string { return locked ? 'Locked' : STATUS_LABEL[status as keyof typeof STATUS_LABEL] ?? status; }

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.service.context().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (c) => { this.ctx.set(c); this.preselect(); },
      error: () => this.failed.set(true),
    });
  }

  private preselect(): void {
    const q = this.route.snapshot.queryParamMap;
    const wantEs = Number(q.get('examSubjectId')); const wantSec = Number(q.get('sectionId'));
    const hit = this.rows().find((r) => r.examSubjectId === wantEs && r.sectionId === wantSec) ?? this.rows().find((r) => r.canEnter) ?? this.rows()[0];
    if (!hit) return;
    this.schemeId.set(hit.schemeId); this.examId.set(hit.examId); this.sectionId.set(hit.sectionId); this.examSubjectId.set(hit.examSubjectId);
    this.reload(false);
  }

  protected async pick(level: 'scheme' | 'exam' | 'section' | 'subject', el: HTMLSelectElement): Promise<void> {
    if (this.dirtyCount() > 0 && !(await this.confirm.confirm({ title: 'Unsaved marks', message: 'You have marks that are not saved. Leave them behind?', confirmText: 'Leave them' }))) {
      el.value = String({ scheme: this.schemeId(), exam: this.examId(), section: this.sectionId(), subject: this.examSubjectId() }[level]);   // snap the picker back
      return;
    }
    const v = Number(el.value);
    if (level === 'scheme') this.schemeId.set(v);
    if (level === 'scheme') this.examId.set(this.exams()[0]?.examId ?? 0);
    if (level === 'exam') this.examId.set(v);
    if (level === 'scheme' || level === 'exam') this.sectionId.set(this.sections()[0]?.sectionId ?? 0);
    if (level === 'section') this.sectionId.set(v);
    if (level !== 'subject') this.examSubjectId.set(this.subjects()[0]?.examSubjectId ?? 0); else this.examSubjectId.set(v);
    this.edits.set({}); this.upload.set(null); this.stale.set(false); this.reopening.set(false);
    this.reload(false);
  }

  /** Loads the grid. keepEdits: keep what the user typed (after a stale-version banner) instead of starting clean. */
  protected reload(keepEdits: boolean): void {
    const es = this.examSubjectId(); const sec = this.sectionId();
    if (!es || !sec) { this.grid.set(null); return; }
    this.gridLoading.set(true); this.stale.set(false);
    if (!keepEdits) this.edits.set({});
    this.service.grid(es, sec).pipe(finalize(() => this.gridLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (g) => this.grid.set(g), error: () => this.grid.set(null) });
  }

  // ---------------------------------------------------------------- reading a cell (saved value with the unsaved edit laid over it)
  protected view(s: GridStudent, c: GridCell): { state: EntryState | 'NONE' | null; marks: string; grade: string } {
    const e = this.edits()[cellKey(s.studentId, c.skillId)];
    if (e) return { state: e.state, marks: e.marks, grade: e.gradeCode };
    return { state: c.state ?? null, marks: c.marks != null ? String(c.marks) : '', grade: c.gradeCode ?? '' };
  }
  protected markText(s: GridStudent, c: GridCell): string { const v = this.view(s, c); return v.state === 'ABSENT' || v.state === 'EXEMPT' ? '' : v.marks; }
  protected gradeText(s: GridStudent, c: GridCell): string { const v = this.view(s, c); return v.state === 'ABSENT' ? 'AB' : v.state === 'EXEMPT' ? 'EX' : v.grade; }
  protected skillName(id: number): string { return this.grid()?.skills.find((k) => k.skillId === id)?.name ?? ''; }
  protected isBad(s: GridStudent, c: GridCell): boolean {
    const g = this.grid(); const v = this.view(s, c);
    if (!g || g.entryMode !== 'MARKS' || v.state !== 'ENTERED' || v.marks.trim() === '') return false;
    const n = Number(v.marks);
    return !/^\d+(\.\d{1,2})?$/.test(v.marks.trim()) || Number.isNaN(n) || n < 0 || n > (g.maxMarks ?? 0);
  }

  // ---------------------------------------------------------------- editing
  private setEdit(s: GridStudent, c: GridCell, e: CellEdit): void { this.edits.update((m) => ({ ...m, [cellKey(s.studentId, c.skillId)]: e })); }
  protected typeMarks(s: GridStudent, c: GridCell, value: string): void { this.setEdit(s, c, { state: 'ENTERED', marks: value, gradeCode: '' }); }
  protected toggle(s: GridStudent, c: GridCell, state: 'ABSENT' | 'EXEMPT'): void {
    this.setEdit(s, c, this.view(s, c).state === state ? { state: 'ENTERED', marks: '', gradeCode: '' } : { state, marks: '', gradeCode: '' });
  }
  protected pickGrade(s: GridStudent, c: GridCell, v: string): void {
    this.setEdit(s, c, v === 'AB' ? { state: 'ABSENT', marks: '', gradeCode: '' } : v === 'EX' ? { state: 'EXEMPT', marks: '', gradeCode: '' } : { state: 'ENTERED', marks: '', gradeCode: v });
  }
  /** Enter moves down the same column. */
  protected down(ev: Event, row: number, col: number): void {
    ev.preventDefault();
    const next = this.host.nativeElement.querySelector<HTMLElement>(`[data-cell="${row + 1}-${col}"]:not([disabled])`);
    (next ?? (ev.target as HTMLElement)).focus();
  }

  // ---------------------------------------------------------------- actions
  protected saveDraft(then?: () => void): void {
    const g = this.grid(); if (!g || this.busy()) return;
    const entries = editsToEntries(g, this.edits());
    if (entries.length === 0) { then?.(); return; }
    this.busy.set(true);
    this.service.save(g.examSubjectId, g.sectionId, g.version, entries).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (fresh) => { this.grid.set(fresh); this.edits.set({}); this.toast.success('Draft saved.'); this.markContext(fresh); then?.(); },
      error: (e: unknown) => { if ((e as HttpErrorResponse)?.status === 409) this.stale.set(true); },
    });
  }

  protected async submit(): Promise<void> {
    const g = this.grid(); if (!g) return;
    const ok = await this.confirm.confirm({ title: 'Submit for review', message: `Submit ${g.subjectName} (${g.examName}, ${g.className} ${g.sectionName})? Nobody can change the marks until it is reopened.`, confirmText: 'Submit' });
    if (!ok) return;
    this.saveDraft(() => this.run(() => this.service.submit(g.examSubjectId, g.sectionId, this.grid()!.version), 'Submitted for review.'));
  }
  protected async accept(): Promise<void> {
    const g = this.grid(); if (!g) return;
    this.run(() => this.service.accept(g.examSubjectId, g.sectionId, g.version), 'Accepted.');
  }
  protected reopen(): void {
    const g = this.grid(); if (!g) return;
    this.run(() => this.service.reopen(g.examSubjectId, g.sectionId, g.version, this.reason().trim()), 'Reopened.', () => { this.reopening.set(false); this.reason.set(''); });
  }
  private run(call: () => Observable<MarksGrid>, message: string, after?: () => void): void {
    this.busy.set(true);
    call().pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (fresh) => { this.grid.set(fresh); this.toast.success(message); this.markContext(fresh); after?.(); },
      error: (e: unknown) => { if ((e as HttpErrorResponse)?.status === 409) this.stale.set(true); },
    });
  }
  /** Keeps the subject picker's status text current without another round trip. */
  private markContext(g: MarksGrid): void {
    this.ctx.update((c) => c && { ...c, rows: c.rows.map((r: MarksContextRow) => (r.examSubjectId === g.examSubjectId && r.sectionId === g.sectionId ? { ...r, status: g.status } : r)) });
  }

  // ---------------------------------------------------------------- Excel
  protected downloadTemplate(): void {
    const g = this.grid(); if (!g) return;
    this.service.template(g.examSubjectId, g.sectionId).subscribe({ next: (b) => saveBlob(b, `marks-${g.className}-${g.sectionName}-${g.examName}-${g.subjectName}.xlsx`.replace(/[\\/:*?"<>| ]+/g, '-')), error: (e: Error) => this.toast.error(e.message) });
  }
  protected onFile(ev: Event): void {
    const input = ev.target as HTMLInputElement; const file = input.files?.[0]; const g = this.grid();
    input.value = '';
    if (!file || !g) return;
    if (!/\.xlsx$/i.test(file.name)) { this.toast.error('Only .xlsx files can be uploaded. Download the template, fill it in and save it as an Excel Workbook (.xlsx).'); return; }
    if (file.size > 1024 * 1024) { this.toast.error('That file is larger than 1 MB.'); return; }
    this.service.uploadPreview(g.examSubjectId, g.sectionId, file).subscribe({ next: (u) => this.upload.set(u), error: () => undefined });
  }
  protected applyUpload(u: UploadPreview): void {
    const next = { ...this.edits() };
    for (const e of u.entries) {
      const key = cellKey(e.studentId, e.skillId);
      next[key] = e.state === 'ABSENT' || e.state === 'EXEMPT' ? { state: e.state, marks: '', gradeCode: '' } : { state: 'ENTERED', marks: e.marks != null ? String(e.marks) : '', gradeCode: e.gradeCode ?? '' };
    }
    this.edits.set(next); this.upload.set(null);
    this.toast.success('The grid is filled from the file. Check it, then Save draft.');
  }
}
