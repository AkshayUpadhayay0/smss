import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent } from '../../../../shared/components';
import { saveBlob } from '../../../homework/utils/homework-format';
import { MyReportCard, MyReportCardDetail, RESULT_LABEL, ResultStatus } from '../../models/results.model';
import { ResultsService } from '../../services/results.service';

/** A Student's own published report cards: the current version of each, as a PDF and as a simple on-screen summary. Drafts and older versions are never shown. */
@Component({
  selector: 'app-my-report-cards',
  imports: [ButtonComponent, CardComponent, IconComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="My Report Cards" subtitle="Your published report cards" icon="file-text" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading your report cards…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">Your report cards could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (cards().length === 0) {
      <app-card><p class="note pad">No report card has been published for you yet. It will appear here when your school publishes it.</p></app-card>
    } @else {
      @for (c of cards(); track c.publicationId) {
        <app-card [title]="(c.termName ?? 'Whole year') + ' · ' + c.schemeName">
          <div class="bar">
            <span>
              <span class="pill" [class.green]="c.result === 'PASS'" [class.red]="c.result === 'FAIL'">{{ label(c.result) }}</span>
              @if (c.percent !== null && c.percent !== undefined) { <span> {{ c.percent.toFixed(2) }}%</span> }
              @if (c.grade) { <span> · Grade {{ c.grade }}</span> }
              @if (c.rank) { <span> · Rank {{ c.rank }}</span> }
              <span class="note"> · {{ c.className }} {{ c.sectionName }} · version {{ c.version }} · {{ c.publishedAt.slice(0, 10) }}</span>
            </span>
            <span class="row-actions">
              <app-button variant="secondary" size="sm" (click)="toggle(c.publicationId)">{{ open() === c.publicationId ? 'Hide summary' : 'View summary' }}</app-button>
              <app-button size="sm" [disabled]="busy()" (click)="download(c)"><app-icon name="download" [size]="14" />Download PDF</app-button>
            </span>
          </div>
          @if (open() === c.publicationId) {
            @if (detail(); as d) {
              <div class="table-wrap">
                <table class="dense" [attr.aria-label]="'Summary of ' + (c.termName ?? 'the year')">
                  <thead><tr><th>Subject</th>@for (t of d.model.terms; track t.termId) { <th>{{ t.name }}</th> }</tr></thead>
                  <tbody>
                    @for (s of d.model.subjects; track s.name) {
                      <tr><td>{{ s.name }}</td>@for (t of d.model.terms; track t.termId) { <td>{{ cell(s, t.termId) }}</td> }</tr>
                    }
                  </tbody>
                </table>
              </div>
              @if (d.model.promotion.decision) {
                <p class="note">Promotion: {{ decision(d.model.promotion.decision) }}@if (d.model.promotion.promotedTo) { to {{ d.model.promotion.promotedTo }} }@if (d.model.promotion.newSessionDate) { · new session begins {{ d.model.promotion.newSessionDate }} }</p>
              }
            } @else { <p class="note" role="status">Loading the summary…</p> }
          }
        </app-card>
      }
    }
  `,
  styles: `.pill.red { background: var(--color-danger-soft, #fde8e8); color: var(--color-danger, #b91c1c); }`,
})
export class MyReportCardsComponent implements OnInit {
  private readonly results = inject(ResultsService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'My Report Cards' }];
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly cards = signal<MyReportCard[]>([]);
  protected readonly open = signal(0);
  protected readonly detail = signal<MyReportCardDetail | null>(null);

  protected label(r: ResultStatus): string { return RESULT_LABEL[r]; }
  protected decision(d: string): string { return d === 'PROMOTED' ? 'Promoted' : d === 'NOT_PROMOTED' ? 'Not promoted' : d === 'DETAINED' ? 'Detained' : d; }
  protected cell(s: MyReportCardDetail['model']['subjects'][number], termId: number): string {
    const t = s.terms.find((x) => x.termId === termId);
    return t ? `${t.obtained}/${t.outOf}${t.grade ? ' · ' + t.grade : ''}` : '—';
  }

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true); this.failed.set(false);
    this.results.myCards().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.cards.set(c), error: () => this.failed.set(true) });
  }

  protected toggle(id: number): void {
    if (this.open() === id) { this.open.set(0); this.detail.set(null); return; }
    this.open.set(id); this.detail.set(null);
    this.results.myCard(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (d) => { if (this.open() === id) this.detail.set(d); }, error: () => this.open.set(0) });
  }

  protected download(c: MyReportCard): void {
    this.busy.set(true);
    this.results.myCardPdf(c.publicationId).pipe(finalize(() => this.busy.set(false))).subscribe({ next: (b) => saveBlob(b, `ReportCard_${(c.termName ?? 'Year').replace(/\W+/g, '_')}_v${c.version}.pdf`), error: () => undefined });
  }
}
