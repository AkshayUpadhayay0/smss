import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { ScheduleScheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

interface Draft { date: string; classDates: Record<number, string> }

/** The exam schedule: one date per exam, and where a class sits on another day, a date for that class. Dates only (no seating, no invigilation). School Admin edits; others read. */
@Component({
  selector: 'app-exam-schedule',
  imports: [ButtonComponent, CardComponent, PageHeaderComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-page-header title="Exam Schedule" subtitle="When each exam is held" icon="calendar" [breadcrumbs]="breadcrumbs" />

    @if (loading()) {
      <app-card><p class="note" role="status">Loading the exam schedule…</p></app-card>
    } @else if (failed()) {
      <app-card><div class="note"><p role="alert">The exam schedule could not be loaded.</p><app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></div></app-card>
    } @else if (schemes().length === 0) {
      <app-card><p class="note">No exams are set up yet.{{ canEdit() ? '' : ' The School Admin sets them up under Examinations.' }} @if (canEdit()) { <a routerLink="/examinations/schemes">Create a report scheme</a> }</p></app-card>
    } @else {
      <div class="stack">
        @for (s of schemes(); track s.schemeId) {
          <app-card [title]="s.schemeName">
            <p class="note">{{ s.academicYearName }} · {{ s.classNames.length ? s.classNames.join(', ') : 'no classes yet' }}</p>
            <div class="table-wrap">
              <table class="dense" [attr.aria-label]="'Exam dates of ' + s.schemeName">
                <thead><tr><th>Term</th><th>Exam</th><th>Date</th>@if (canEdit()) { <th>Different for a class</th> }</tr></thead>
                <tbody>
                  @for (e of s.exams; track e.examId) {
                    <tr>
                      <td>{{ e.termName }}</td>
                      <td>{{ e.examName }}</td>
                      <td>
                        @if (canEdit()) { <input type="date" [value]="draft()[e.examId]?.date" (change)="setDate(e.examId, $any($event.target).value)" [attr.aria-label]="e.examName + ' date'" /> }
                        @else { {{ e.examDate ? fmt(e.examDate) : 'Not set' }}{{ e.classDates.length ? ' (some classes differ)' : '' }} }
                      </td>
                      @if (canEdit()) {
                        <td>
                          <div class="dense classdates">
                            @for (cd of classOptions(s); track cd.classId) {
                              <label class="check">{{ cd.className }} <input type="date" [value]="draft()[e.examId]?.classDates?.[cd.classId] ?? ''" (change)="setClassDate(e.examId, cd.classId, $any($event.target).value)" [attr.aria-label]="e.examName + ' date for ' + cd.className" /></label>
                            }
                          </div>
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (canEdit()) {
              <div class="row-actions" style="margin-top: var(--space-4)"><app-button [disabled]="savingId() === s.schemeId" (click)="save(s)">{{ savingId() === s.schemeId ? 'Saving…' : 'Save dates' }}</app-button></div>
            }
          </app-card>
        }
      </div>
    }
  `,
  styles: `.classdates { display: flex; flex-wrap: wrap; gap: var(--space-3); }`,
})
export class ExamScheduleComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'Examinations' }, { label: 'Exam Schedule' }];
  protected readonly canEdit = computed(() => canAccess('/examinations/schemes/new', this.auth.currentUserRoles()));
  protected readonly schemes = signal<ScheduleScheme[]>([]);
  protected readonly draft = signal<Record<number, Draft>>({});
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly savingId = signal<number | null>(null);
  protected fmt(d: string): string { return new Date(`${d}T00:00:00`).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }); }

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.service.schedule().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.apply(l), error: () => this.failed.set(true) });
  }

  private apply(list: ScheduleScheme[]): void {
    this.schemes.set(list);
    const d: Record<number, Draft> = {};
    for (const s of list) for (const e of s.exams) d[e.examId] = { date: e.examDate ?? '', classDates: Object.fromEntries(e.classDates.map((c) => [c.classId, c.examDate])) };
    this.draft.set(d);
  }

  /** The scheme's classes, each of which can sit an exam on another day. */
  protected classOptions(s: ScheduleScheme): { classId: number; className: string }[] { return s.classes; }

  protected setDate(examId: number, v: string): void { this.draft.update((d) => ({ ...d, [examId]: { ...d[examId], date: v } })); }
  protected setClassDate(examId: number, classId: number, v: string): void {
    this.draft.update((d) => ({ ...d, [examId]: { ...d[examId], classDates: { ...d[examId].classDates, [classId]: v } } }));
  }

  protected save(s: ScheduleScheme): void {
    this.savingId.set(s.schemeId);
    const body = {
      schemeId: s.schemeId,
      exams: s.exams.map((e) => {
        const d = this.draft()[e.examId];
        return { examId: e.examId, examDate: d?.date || null, classDates: Object.entries(d?.classDates ?? {}).filter(([, v]) => !!v).map(([k, v]) => ({ classId: Number(k), examDate: v })) };
      }),
    };
    this.service.saveSchedule(body).pipe(finalize(() => this.savingId.set(null))).subscribe({
      next: (saved) => { this.toast.success('Exam dates saved.'); this.apply(this.schemes().map((x) => (x.schemeId === saved.schemeId ? saved : x))); },
      error: () => undefined,
    });
  }
}
