import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { ClassActivityTabComponent } from '../../components/class-activity-tab/class-activity-tab.component';
import { ClassPromotionTabComponent } from '../../components/class-promotion-tab/class-promotion-tab.component';
import { ClassRemarksTabComponent } from '../../components/class-remarks-tab/class-remarks-tab.component';
import { CellStatus, Completeness, CompletenessCell, CompletenessTerm, MarksContextRow, STATUS_LABEL, distinct } from '../../models/marks.model';
import { MarksService } from '../../services/marks.service';

type Tab = 'marks' | 'activity' | 'remarks' | 'promotion';

/**
 * My Class Report: how complete the section's marks are, the class teacher's own inputs (activity grades, remarks and attendance, promotion), and the term lock.
 * The School Admin locks and unlocks; the Principal only reads; everyone else who gets here is the section's class teacher.
 */
@Component({
  selector: 'app-class-report',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, RouterLink, ClassActivityTabComponent, ClassRemarksTabComponent, ClassPromotionTabComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="My Class Report" subtitle="Marks completeness, remarks, attendance and promotion for one section" icon="file-text" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading your sections…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The class report could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (options().length === 0) {
      <app-card><p class="note pad">There is no section for you to review yet. The class report is for a section's class teacher (and the School Admin and Principal), once a report scheme covers its class.</p></app-card>
    } @else {
      <app-card>
        <div class="grid-2">
          @if (schemes().length > 1) {
            <label class="field"><span>Report scheme</span>
              <select (change)="setScheme($any($event.target).value)" aria-label="Report scheme">
                @for (s of schemes(); track s.schemeId) { <option [value]="s.schemeId" [selected]="s.schemeId === schemeId()">{{ s.schemeName }}</option> }
              </select></label>
          }
          <label class="field"><span>Section</span>
            <select (change)="setSection($any($event.target).value)" aria-label="Section">
              @for (s of sections(); track s.sectionId) { <option [value]="s.sectionId" [selected]="s.sectionId === sectionId()">{{ s.className }} {{ s.sectionName }}</option> }
            </select></label>
        </div>
      </app-card>

      @if (comp(); as c) {
        <div class="tabs" role="tablist" aria-label="Class report">
          @for (t of tabs; track t.key) { <button type="button" role="tab" class="tab" [class.on]="tab() === t.key" [attr.aria-selected]="tab() === t.key" (click)="tab.set(t.key)">{{ t.label }}</button> }
        </div>

        @switch (tab()) {
          @case ('marks') {
            <p class="note" style="margin-bottom: var(--space-4)">{{ c.className }} {{ c.sectionName }} · {{ c.rosterCount }} students · {{ c.evaluationMode === 'MARKS' ? 'Marks' : 'Grades only' }} scheme</p>
            @for (t of c.terms; track t.termId) {
              <app-card [title]="t.termName">
                <div class="bar">
                  <span>
                    <span class="pill" [class.green]="t.locked" [class.blue]="!t.locked && t.readyToLock">{{ t.locked ? 'Locked' : t.readyToLock ? 'Ready to lock' : 'In progress' }}</span>
                    <span class="note"> {{ t.accepted }} of {{ t.cells }} accepted@if (t.submitted) { · {{ t.submitted }} waiting for you }@if (t.draft) { · {{ t.draft }} in draft }@if (t.notStarted) { · {{ t.notStarted }} not started }</span>
                  </span>
                  @if (c.canLock) {
                    <span class="row-actions">
                      @if (!t.locked) { <app-button size="sm" [disabled]="!t.readyToLock || busy()" (click)="lock(t)"><app-icon name="lock" [size]="14" />Lock term</app-button> }
                      @else { <app-button variant="secondary" size="sm" [disabled]="busy()" (click)="unlocking.set(t.termId)">Unlock…</app-button> }
                    </span>
                  }
                </div>
                @if (t.locked) { <p class="note">Locked: marks and the class teacher's inputs for this term can't change. Unlocking does not make accepted marks editable by itself; a grid still has to be reopened with a reason.</p> }
                @else if (t.lastUnlockReason) { <p class="note">Last unlocked because: {{ t.lastUnlockReason }}</p> }
                @if (unlocking() === t.termId) {
                  <div class="warnings" role="group" aria-label="Unlock">
                    <label class="field"><span>Why are you unlocking {{ t.termName }}? (kept in the audit trail)</span><input type="text" maxlength="300" [value]="reason()" (input)="reason.set($any($event.target).value)" /></label>
                    <div class="row-actions"><app-button size="sm" [disabled]="reason().trim().length < 3 || busy()" (click)="unlock(t)">Unlock</app-button><app-button variant="secondary" size="sm" (click)="unlocking.set(0)">Cancel</app-button></div>
                  </div>
                }
                <div class="table-wrap">
                  <table class="dense" [attr.aria-label]="t.termName + ' completeness'">
                    <thead><tr><th>Subject</th>@for (e of t.exams; track e.examId) { <th>{{ e.name }}</th> }</tr></thead>
                    <tbody>
                      @for (s of t.subjects; track s.schemeSubjectId) {
                        <tr>
                          <td>{{ s.subjectName }}</td>
                          @for (e of t.exams; track e.examId) {
                            <td>
                              @if (cell(s.cells, e.examId); as k) {
                                <a class="chip" [class.ok]="k.status === 'ACCEPTED'" [class.wait]="k.status === 'SUBMITTED'" [routerLink]="['/examinations/marks']" [queryParams]="{ examSubjectId: k.examSubjectId, sectionId: c.sectionId }" [attr.aria-label]="s.subjectName + ' ' + e.name + ': ' + label(k.status)">
                                  {{ label(k.status) }}@if (k.missing > 0 && k.status !== 'NOT_STARTED') { <small> · {{ k.missing }} missing</small> }
                                </a>
                              } @else { <span class="note">—</span> }
                            </td>
                          }
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </app-card>
            }
          }
          @case ('activity') { <app-card><div class="bar"><label class="field"><span>Term</span><select (change)="termId.set(+$any($event.target).value)" aria-label="Term">@for (t of c.terms; track t.termId) { <option [value]="t.termId" [selected]="t.termId === activeTerm()">{{ t.termName }}</option> }</select></label></div>
            <app-class-activity-tab [termId]="activeTerm()" [sectionId]="c.sectionId" /></app-card> }
          @case ('remarks') { <app-card><div class="bar"><label class="field"><span>Term</span><select (change)="termId.set(+$any($event.target).value)" aria-label="Term">@for (t of c.terms; track t.termId) { <option [value]="t.termId" [selected]="t.termId === activeTerm()">{{ t.termName }}</option> }</select></label></div>
            <app-class-remarks-tab [termId]="activeTerm()" [sectionId]="c.sectionId" /></app-card> }
          @case ('promotion') { <app-card><app-class-promotion-tab [schemeId]="c.schemeId" [sectionId]="c.sectionId" /></app-card> }
        }
      } @else if (compLoading()) {
        <app-card><p class="note" role="status">Loading the section…</p></app-card>
      }
    }
  `,
  styles: `
    .tabs { display: flex; gap: var(--space-2); flex-wrap: wrap; margin: var(--space-4) 0; }
    .tab { border: 1px solid var(--color-border); background: var(--color-surface, #fff); border-radius: 999px; padding: var(--space-2) var(--space-4); font: inherit; cursor: pointer; }
    .tab.on { background: var(--color-primary); border-color: var(--color-primary); color: #fff; }
    .chip { display: inline-block; padding: 2px var(--space-3); border-radius: 999px; font-size: var(--text-xs); text-decoration: none; color: var(--color-text); background: var(--color-status-inactive-bg); white-space: nowrap; }
    .chip.ok { background: var(--color-success-soft); color: var(--color-status-active-fg); }
    .chip.wait { background: var(--color-info-soft); color: var(--color-info); }
  `,
})
export class ClassReportComponent implements OnInit {
  private readonly service = inject(MarksService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly router = inject(Router);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'My Class Report' }];
  protected readonly tabs: { key: Tab; label: string }[] = [{ key: 'marks', label: 'Marks' }, { key: 'activity', label: 'Activity grades' }, { key: 'remarks', label: 'Remarks and attendance' }, { key: 'promotion', label: 'Promotion' }];
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly compLoading = signal(false);
  protected readonly busy = signal(false);
  protected readonly rows = signal<MarksContextRow[]>([]);
  protected readonly schemeId = signal(0);
  protected readonly sectionId = signal(0);
  protected readonly comp = signal<Completeness | null>(null);
  protected readonly tab = signal<Tab>('marks');
  protected readonly termId = signal(0);
  protected readonly unlocking = signal(0);
  protected readonly reason = signal('');

  /** The sections this user may open: the School Admin and the Principal see every section; a teacher only the ones they review (class teacher). */
  protected readonly options = computed(() => {
    const roles = this.auth.currentUserRoles();
    const wide = roles.includes(ROLES.SCHOOL_ADMIN) || roles.includes(ROLES.PRINCIPAL);
    return distinct(this.rows().filter((r) => wide || r.canReview), (r) => `${r.schemeId}|${r.sectionId}`);
  });
  protected readonly schemes = computed(() => distinct(this.options(), (r) => r.schemeId));
  protected readonly sections = computed(() => this.options().filter((r) => r.schemeId === this.schemeId()));
  protected readonly activeTerm = computed(() => this.termId() || this.comp()?.terms[0]?.termId || 0);

  protected label(s: CellStatus): string { return STATUS_LABEL[s]; }
  protected cell(cells: CompletenessCell[], examId: number): CompletenessCell | undefined { return cells.find((c) => c.examId === examId); }

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.service.context().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (c) => { this.rows.set(c.rows); const first = this.options()[0]; if (first) { this.schemeId.set(first.schemeId); this.sectionId.set(first.sectionId); this.refresh(); } },
      error: () => this.failed.set(true),
    });
  }
  protected setScheme(v: string): void { this.schemeId.set(Number(v)); this.sectionId.set(this.sections()[0]?.sectionId ?? 0); this.termId.set(0); this.refresh(); }
  protected setSection(v: string): void { this.sectionId.set(Number(v)); this.termId.set(0); this.refresh(); }

  protected refresh(): void {
    if (!this.schemeId() || !this.sectionId()) { this.comp.set(null); return; }
    this.compLoading.set(true);
    this.service.completeness(this.schemeId(), this.sectionId()).pipe(finalize(() => this.compLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.comp.set(c), error: () => this.comp.set(null) });
  }

  protected async lock(t: CompletenessTerm): Promise<void> {
    const c = this.comp(); if (!c) return;
    const ok = await this.confirm.confirm({ title: 'Lock the term', message: `Lock ${t.termName} for ${c.className} ${c.sectionName}? Marks, remarks, attendance and grades for this term can't change until a School Admin unlocks it.`, confirmText: 'Lock' });
    if (!ok) return;
    this.sendLock(t, c.sectionId, false);
  }

  /** A lock that would leave out students added after the marks were accepted first answers 409 with their names; the admin confirms and it is sent again. */
  private sendLock(t: CompletenessTerm, sectionId: number, confirmExcluded: boolean): void {
    this.busy.set(true);
    this.service.lock(t.termId, sectionId, confirmExcluded).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (r) => { this.toast.success(r.excluded?.length ? `${t.termName} locked. ${r.excluded.length} late student(s) left out.` : `${t.termName} locked.`); this.refresh(); },
      error: async (e: HttpErrorResponse) => {
        const body = e.error as { message?: string; data?: { needsConfirmation?: boolean; excluded?: { name: string }[] } } | null;
        if (e.status === 409 && body?.data?.needsConfirmation) {
          const names = (body.data.excluded ?? []).map((x) => x.name);
          const ok = await this.confirm.confirm({ title: 'Lock without these students?', message: `${names.length} student${names.length === 1 ? '' : 's'} joined after the marks were accepted and ${names.length === 1 ? 'has' : 'have'} no marks for ${t.termName}: ${names.join(', ')}. They will be left out of this term's results and included in a later version once their marks are entered and the term is locked again.`, confirmText: 'Lock anyway' });
          if (ok) this.sendLock(t, sectionId, true);
          return;
        }
        this.toast.error(body?.message ?? 'The term could not be locked.');
        this.refresh();
      },
    });
  }
  protected unlock(t: CompletenessTerm): void {
    const c = this.comp(); if (!c) return;
    this.busy.set(true);
    this.service.unlock(t.termId, c.sectionId, this.reason().trim()).pipe(finalize(() => this.busy.set(false))).subscribe({ next: () => { this.toast.success(`${t.termName} unlocked.`); this.unlocking.set(0); this.reason.set(''); this.refresh(); }, error: () => undefined });
  }
}
