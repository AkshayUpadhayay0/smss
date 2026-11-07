import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ROLES } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { saveBlob } from '../../../homework/utils/homework-format';
import { MarksContextRow, distinct } from '../../models/marks.model';
import { Publication, PublicationDetail, PublishRequest, PublishResult, RESULT_LABEL, ResultPreview, ResultStatus } from '../../models/results.model';
import { MarksService } from '../../services/marks.service';
import { BulkDownloadError, ResultsService } from '../../services/results.service';
import { BulkPart } from '../../models/report-card-settings.model';

/**
 * Results and Publish. Pick a scheme, a section and a term (or the whole year): the table is the live result; Publish stores it as an immutable numbered version.
 * The School Admin publishes. The Principal and the section's class teacher read (the class teacher can also look at a DRAFT card). Everything the screen shows comes from the API.
 */
@Component({
  selector: 'app-results-publish',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="Results and Publish" subtitle="Check a section's results, then publish the report cards" icon="file-text" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading your sections…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The sections could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (options().length === 0) {
      <app-card><p class="note pad">There is no section to show yet. Results appear once a report scheme covers a class and its marks have been entered.</p></app-card>
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
          <label class="field"><span>Results for</span>
            <select (change)="setScope($any($event.target).value)" aria-label="Results for">
              @for (t of terms(); track t.termId) { <option [value]="'T' + t.termId" [selected]="scope() === 'TERM' && termId() === t.termId">{{ t.termName }}</option> }
              <option value="YEAR" [selected]="scope() === 'YEAR'">Whole year</option>
            </select></label>
        </div>
      </app-card>

      @if (pv(); as p) {
        @if (!p.templateAvailable) {
          <div class="warnings" role="alert"><strong>{{ p.templateMessage }}</strong> The design for this scheme can be changed under Examinations, Report Schemes.</div>
        }
        @if (p.unlockedTerms.length) {
          <div class="warnings" role="status"><strong>Lock the term first.</strong> Not locked for this section: {{ p.unlockedTerms.join(', ') }}. <a routerLink="/examinations/class-report">Open My Class Report</a>.</div>
        }
        @if (incomplete().length) {
          <div class="warnings" role="status">
            <strong>{{ incomplete().length }} student{{ incomplete().length === 1 ? '' : 's' }} left out.</strong> Their results are incomplete, so they won't get a card in this version: {{ names(incomplete()) }}.
            They will be included in a later version once their marks are entered and the term is locked again.
          </div>
        }
        @if (p.unmarkedDates.length) {
          <div class="warnings" role="status"><strong>Attendance was not marked on {{ p.unmarkedDates.length }} school day{{ p.unmarkedDates.length === 1 ? '' : 's' }}.</strong> Those days are not counted in the attendance printed on the cards.</div>
        }

        <app-card [title]="p.className + ' ' + p.sectionName + ' · ' + (p.termName ?? 'Whole year')">
          <div class="bar">
            <span class="note">
              @if (p.current; as cur) { Current published version: v{{ cur.version }} · {{ cur.publishedAt.slice(0, 10) }} · {{ cur.studentCount }} cards } @else { Nothing published yet for this selection. }
            </span>
            <span class="row-actions">
              @if (p.canPublish) {
                <app-button size="sm" [disabled]="busy() || !publishable()" (click)="publish()"><app-icon name="upload" [size]="14" />Publish{{ p.current ? ' new version' : '' }}</app-button>
              } @else {
                <span class="note">{{ readOnlyReason() }}</span>
              }
            </span>
          </div>
          <div class="table-wrap">
            <table class="dense" aria-label="Results">
              <thead>
                <tr>
                  <th>Roll</th><th>Student</th>
                  @if (p.mode === 'MARKS') { <th>Total</th><th>Percentage</th> }
                  <th>Grade</th><th>Result</th>
                  @if (p.showRank) { <th>Rank</th> }
                  @if (p.canDownloadDraft) { <th><span class="sr-only">Draft card</span></th> }
                </tr>
              </thead>
              <tbody>
                @for (s of p.students; track s.studentId) {
                  <tr>
                    <td>{{ s.rollNumber }}</td><td>{{ s.name }}</td>
                    @if (p.mode === 'MARKS') { <td class="nowrap">{{ s.result === 'INCOMPLETE' ? '—' : s.obtained + ' / ' + s.outOf }}</td><td class="nowrap">{{ pct(s.percent) }}</td> }
                    <td>{{ s.grade ?? '—' }}</td>
                    <td><span class="pill" [class.green]="s.result === 'PASS'" [class.red]="s.result === 'FAIL'">{{ label(s.result) }}</span>@if (s.failedSubjects.length) { <small class="note"> {{ s.failedSubjects.join(', ') }}</small> }</td>
                    @if (p.showRank) { <td>{{ s.rank ?? '—' }}</td> }
                    @if (p.canDownloadDraft) { <td><app-button variant="secondary" size="sm" [disabled]="busy()" (click)="draft(s.studentId, s.name)" [attr.aria-label]="'Draft card for ' + s.name">Draft PDF</app-button></td> }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </app-card>

        <app-card title="Published versions">
          @if (versions().length === 0) { <p class="note">No version has been published for this section yet.</p> }
          @for (v of versions(); track v.publicationId) {
            <div class="version" [class.old]="v.status === 'SUPERSEDED'">
              <div class="bar">
                <span>
                  <strong>v{{ v.version }}</strong> · {{ v.termName ?? 'Whole year' }}
                  <span class="pill" [class.green]="v.status === 'PUBLISHED'">{{ v.status === 'PUBLISHED' ? 'Current' : 'Superseded' }}</span>
                  <span class="note"> {{ v.publishedAt.slice(0, 10) }} · {{ v.studentCount }} cards@if (v.excludedCount) { · {{ v.excludedCount }} left out }@if (v.note) { · {{ v.note }} }</span>
                </span>
                <span class="row-actions">
                  <app-button variant="secondary" size="sm" [disabled]="busy()" (click)="downloadAll(v)" [attr.aria-label]="'Download all cards of version ' + v.version + ' as one PDF'"><app-icon name="download" [size]="14" />Download all (PDF)</app-button>
                  <app-button variant="secondary" size="sm" (click)="toggle(v.publicationId)">{{ open() === v.publicationId ? 'Hide cards' : 'Cards' }}</app-button>
                </span>
              </div>
              @if (bulkParts(); as bp) {
                @if (bp.publicationId === v.publicationId) {
                  <div class="warnings" role="alert">
                    <strong>This version is too big for one file.</strong> {{ bp.message }}
                    <span class="row-actions">
                      @for (part of bp.parts; track part.fromRoll) {
                        <app-button variant="secondary" size="sm" [disabled]="busy()" (click)="downloadAll(v, part)">Roll {{ part.fromRoll }} to {{ part.toRoll }} ({{ part.count }})</app-button>
                      }
                    </span>
                  </div>
                }
              }
              @if (open() === v.publicationId) {
                @if (detail(); as d) {
                  @if (d.excluded.length) { <p class="note">Left out of this version: {{ names(d.excluded) }}.</p> }
                  <div class="table-wrap">
                    <table class="dense" [attr.aria-label]="'Cards of version ' + v.version">
                      <thead><tr><th>Roll</th><th>Student</th><th>Result</th><th><span class="sr-only">Download</span></th></tr></thead>
                      <tbody>
                        @for (c of d.cards; track c.studentId) {
                          <tr>
                            <td>{{ c.rollNumber }}</td><td>{{ c.name }}</td><td>{{ label(c.result) }}</td>
                            <td><app-button variant="secondary" size="sm" [disabled]="busy()" (click)="download(v, c.studentId, c.admissionNumber)" [attr.aria-label]="'Download card of ' + c.name"><app-icon name="download" [size]="14" />PDF</app-button></td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                } @else { <p class="note" role="status">Loading the cards…</p> }
              }
            </div>
          }
        </app-card>
      } @else if (pvLoading()) {
        <app-card><p class="note" role="status">Calculating the results…</p></app-card>
      } @else if (pvFailed()) {
        <app-card><p class="note" role="alert">The results could not be loaded for this selection.</p></app-card>
      }
    }
  `,
  styles: `
    .version { border-top: 1px solid var(--color-border); padding-top: var(--space-3); margin-top: var(--space-3); }
    .version.old { opacity: .8; }
    .nowrap { white-space: nowrap; }
    .pill.red { background: var(--color-danger-soft, #fde8e8); color: var(--color-danger, #b91c1c); }
    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
  `,
})
export class ResultsPublishComponent implements OnInit {
  private readonly marks = inject(MarksService);
  private readonly results = inject(ResultsService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Results and Publish' }];
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly pvLoading = signal(false);
  protected readonly pvFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly rows = signal<MarksContextRow[]>([]);
  protected readonly schemeId = signal(0);
  protected readonly sectionId = signal(0);
  protected readonly scope = signal<'TERM' | 'YEAR'>('TERM');
  protected readonly termId = signal(0);
  protected readonly pv = signal<ResultPreview | null>(null);
  protected readonly versions = signal<Publication[]>([]);
  protected readonly open = signal(0);
  /** Set when "Download all" was refused because the version is over the server's cap: the parts to download instead. */
  protected readonly bulkParts = signal<{ publicationId: number; message: string; parts: BulkPart[] } | null>(null);
  protected readonly detail = signal<PublicationDetail | null>(null);

  /** Every section for the School Admin and the Principal; a teacher only the sections they are the class teacher of. */
  protected readonly options = computed(() => {
    const roles = this.auth.currentUserRoles();
    const wide = roles.includes(ROLES.SCHOOL_ADMIN) || roles.includes(ROLES.PRINCIPAL);
    return distinct(this.rows().filter((r) => wide || r.canReview), (r) => `${r.schemeId}|${r.sectionId}`);
  });
  protected readonly schemes = computed(() => distinct(this.options(), (r) => r.schemeId));
  protected readonly sections = computed(() => this.options().filter((r) => r.schemeId === this.schemeId()));
  protected readonly terms = computed(() => distinct(this.rows().filter((r) => r.schemeId === this.schemeId()), (r) => r.termId).sort((a, b) => a.termId - b.termId));
  protected readonly incomplete = computed(() => (this.pv()?.students ?? []).filter((s) => s.result === 'INCOMPLETE'));
  protected readonly publishable = computed(() => { const p = this.pv(); return !!p && p.canPublish && p.students.some((s) => s.result !== 'INCOMPLETE'); });
  protected readonly readOnlyReason = computed(() => {
    const p = this.pv();
    if (!p || p.canPublish) return '';
    if (!p.templateAvailable) return p.templateMessage ?? "This design can't print this scheme. Choose another design on Report Card Settings.";
    if (p.unlockedTerms.length) return 'Lock the term first.';
    return this.auth.currentUserRoles().includes(ROLES.SCHOOL_ADMIN) ? '' : 'Only a School Admin can publish.';
  });

  protected label(r: ResultStatus): string { return RESULT_LABEL[r]; }
  protected pct(v: number | null | undefined): string { return v === null || v === undefined ? '—' : v.toFixed(2) + '%'; }
  protected names(list: { name: string }[]): string { const n = list.map((x) => x.name); return n.slice(0, 10).join(', ') + (n.length > 10 ? ` and ${n.length - 10} more` : ''); }

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.marks.context().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (c) => {
        this.rows.set(c.rows);
        const first = this.options()[0];
        if (first) { this.schemeId.set(first.schemeId); this.sectionId.set(first.sectionId); this.termId.set(this.terms()[0]?.termId ?? 0); this.refresh(); }
      },
      error: () => this.failed.set(true),
    });
  }
  protected setScheme(v: string): void { this.schemeId.set(Number(v)); this.sectionId.set(this.sections()[0]?.sectionId ?? 0); this.scope.set('TERM'); this.termId.set(this.terms()[0]?.termId ?? 0); this.refresh(); }
  protected setSection(v: string): void { this.sectionId.set(Number(v)); this.refresh(); }
  protected setScope(v: string): void { if (v === 'YEAR') { this.scope.set('YEAR'); this.termId.set(0); } else { this.scope.set('TERM'); this.termId.set(Number(v.slice(1))); } this.refresh(); }

  protected refresh(): void {
    this.open.set(0); this.detail.set(null);
    if (!this.schemeId() || !this.sectionId()) { this.pv.set(null); this.versions.set([]); return; }
    this.pvLoading.set(true); this.pvFailed.set(false); this.pv.set(null);
    this.results.preview(this.schemeId(), this.sectionId(), this.scope(), this.scope() === 'TERM' ? this.termId() : null)
      .pipe(finalize(() => this.pvLoading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (p) => this.pv.set(p), error: () => this.pvFailed.set(true) });
    this.results.publications(this.schemeId(), this.sectionId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (v) => this.versions.set(v), error: () => this.versions.set([]) });
  }

  protected toggle(id: number): void {
    if (this.open() === id) { this.open.set(0); this.detail.set(null); return; }
    this.open.set(id); this.detail.set(null);
    this.results.publication(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (d) => { if (this.open() === id) this.detail.set(d); }, error: () => this.open.set(0) });
  }

  protected download(v: Publication, studentId: string, admission: string): void {
    this.busy.set(true);
    this.results.cardPdf(v.publicationId, studentId).pipe(finalize(() => this.busy.set(false))).subscribe({ next: (b) => saveBlob(b, `ReportCard_${admission}_v${v.version}.pdf`), error: () => undefined });
  }
  /** One PDF with every card of the version in roll order (or one roll range of it). Slow or refused requests say so plainly; nothing is cut short silently. */
  protected downloadAll(v: Publication, part?: BulkPart): void {
    this.busy.set(true);
    this.bulkParts.set(null);
    this.results.bulkPdf(v.publicationId, part ? { fromRoll: part.fromRoll, toRoll: part.toRoll } : null).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (b) => saveBlob(b, `ReportCards_${v.className}${v.sectionName}_${(v.termName ?? 'Year').replace(/\W+/g, '_')}_v${v.version}${part ? `_rolls_${part.fromRoll}-${part.toRoll}` : ''}.pdf`.replace(/\s+/g, '')),
      error: (e: unknown) => {
        const err = e instanceof BulkDownloadError ? e : null;
        if (err && err.parts.length) this.bulkParts.set({ publicationId: v.publicationId, message: err.message, parts: err.parts });
        else this.toast.error(err?.message ?? 'The report cards could not be prepared. Please try again.');
      },
    });
  }
  protected draft(studentId: string, name: string): void {
    const p = this.pv(); if (!p) return;
    this.busy.set(true);
    this.results.draftPdf(p.schemeId, p.sectionId, p.scope, p.termId, studentId).pipe(finalize(() => this.busy.set(false))).subscribe({ next: (b) => saveBlob(b, `ReportCard_DRAFT_${name.replace(/\W+/g, '_')}.pdf`), error: () => undefined });
  }

  /** Publishing is confirmed first, with the students who will be left out; the server checks again (a 409 asks the same question if something changed meanwhile). */
  protected async publish(): Promise<void> {
    const p = this.pv(); if (!p) return;
    const left = this.incomplete();
    const lines = [`Publish ${p.className} ${p.sectionName}, ${p.termName ?? 'whole year'}, as ${p.current ? 'version ' + (p.current.version + 1) + '? The current version stays available as superseded' : 'version 1'}.`];
    if (left.length) lines.push(`${left.length} student${left.length === 1 ? ' is' : 's are'} left out because the result is incomplete: ${this.names(left)}.`);
    if (p.unmarkedDates.length) lines.push(`Attendance was not marked on ${p.unmarkedDates.length} school day(s); they are not counted.`);
    const ok = await this.confirm.confirm({ title: 'Publish report cards', message: lines.join(' '), confirmText: 'Publish' });
    if (!ok) return;
    this.send({ schemeId: p.schemeId, sectionId: p.sectionId, scope: p.scope, termId: p.termId ?? null, note: null, confirmExcluded: left.length > 0, acknowledgeAttendance: p.unmarkedDates.length > 0 });
  }

  private send(req: PublishRequest): void {
    this.busy.set(true);
    this.results.publish(req).pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (r) => { this.toast.success(r.publication ? `Published version ${r.publication.version}.` : 'Published.'); this.refresh(); },
      error: async (e: HttpErrorResponse) => {
        const body = e.error as { message?: string; data?: PublishResult | null } | null;
        if (e.status === 409 && body?.data?.needsConfirmation) {
          const d = body.data;
          const ok = await this.confirm.confirm({ title: d.reason === 'ATTENDANCE' ? 'Attendance days not marked' : 'Publish without these students?', message: body.message ?? 'Publish anyway?', confirmText: 'Publish anyway' });
          if (ok) this.send({ ...req, confirmExcluded: req.confirmExcluded || d.reason === 'EXCLUDED', acknowledgeAttendance: req.acknowledgeAttendance || d.reason === 'ATTENDANCE' });
          return;
        }
        this.toast.error(body?.message ?? 'The report cards could not be published.');
        this.refresh();
      },
    });
  }
}
