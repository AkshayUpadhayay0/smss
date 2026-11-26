import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { Leaver, LeavingCheck, LeavingCorrectionCheck, LeavingType } from '../../models/student-leaving.model';
import { StudentLeavingService } from '../../services/student-leaving.service';

/**
 * Student leaving (School Admin and Principal): record that a student LEFT or was TRANSFERRED OUT, correct a leaving recorded by mistake, and see who has left. The server decides everything
 * (who may, the dues check, what blocks a correction): this page only asks and shows the answer. A student's status is not changed: "left" is the outcome of their enrolment.
 */
@Component({
  selector: 'app-leaving',
  imports: [CurrencyPipe, DatePipe, RouterLink, ButtonComponent, CardComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Student leaving" subtitle="Record a student who left or was transferred out, correct a mistake, and see who has left" icon="graduation-cap" [breadcrumbs]="[{ label: 'Students', link: '/students' }, { label: 'Student leaving' }]">
      <a routerLink="/transfer-certificates"><app-button variant="secondary">Transfer certificates</app-button></a>
    </app-page-header>

    <app-card title="Record a leaving">
      <p class="hint">The student's login stops working at once and they leave every live class list. Their history stays. Enter the student id and check.</p>
      <div class="row">
        <label class="field"><span>Student id</span><input data-student [value]="studentId()" (input)="studentId.set($any($event.target).value); resetCheck()" autocomplete="off" /></label>
        <label class="field"><span>Leaving date</span><input data-date type="date" [value]="date()" (input)="date.set($any($event.target).value); resetCheck()" /></label>
        <app-button data-check-btn variant="secondary" [disabled]="!studentId().trim() || busy()" (click)="check()">Check</app-button>
      </div>
      @if (checkFailed()) { <p class="issue" role="alert" data-check-failed>That could not be checked. Please try again.</p> }
      @if (result(); as c) {
        <div data-check>
          <p><strong>{{ c.studentName }}</strong> ({{ c.studentId }}) · {{ c.className }} {{ c.sectionName }} · {{ c.yearName }}</p>
          @if (c.blockers.length) { <ul class="issue" data-blockers>@for (b of c.blockers; track b) { <li>{{ b }}</li> }</ul> }
          @if (c.canRecord) {
            @if (c.requiresOverride) {
              <p class="issue" data-dues>Unpaid dues up to the leaving date: {{ c.outstanding | currency: 'INR' : 'symbol' : '1.0-0' }}. They stay owed. To record the leaving anyway, tick the box and give a reason.</p>
              <label class="check"><input type="checkbox" data-override [checked]="overrideDues()" (change)="overrideDues.set($any($event.target).checked)" /> Record anyway</label>
              <label class="field wide"><span>Reason for overriding (at least 10 characters)</span><textarea data-override-reason rows="2" maxlength="500" [value]="overrideReason()" (input)="overrideReason.set($any($event.target).value)"></textarea></label>
            }
            <div class="row">
              <label class="field"><span>Type</span>
                <select data-type (change)="type.set($any($event.target).value)"><option value="LEFT" [selected]="type() === 'LEFT'">Left the school</option><option value="TRANSFERRED_OUT" [selected]="type() === 'TRANSFERRED_OUT'">Transferred out</option></select></label>
            </div>
            <label class="field wide"><span>Reason for leaving</span><textarea data-reason rows="2" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
            <app-button data-record-btn [disabled]="busy() || !reason().trim() || (c.requiresOverride && (!overrideDues() || overrideReason().trim().length < 10))" (click)="record(c)">{{ busy() ? 'Recording…' : 'Record leaving' }}</app-button>
          }
        </div>
      }
    </app-card>

    <app-card title="Correct a leaving">
      <p class="hint">Recorded by mistake? A leaving can be corrected only while the student has no later enrolment and no issued transfer certificate.</p>
      <div class="row">
        <label class="field"><span>Student id</span><input data-fix-student [value]="fixId()" (input)="fixId.set($any($event.target).value); fixResult.set(null)" autocomplete="off" /></label>
        <app-button data-fix-check-btn variant="secondary" [disabled]="!fixId().trim() || busy()" (click)="checkFix()">Check</app-button>
      </div>
      @if (fixResult(); as f) {
        <div data-fix-check>
          @if (f.canCorrect) {
            <p>{{ f.studentName }} ({{ f.studentId }}): {{ f.outcome === 'LEFT' ? 'left' : 'transferred out' }} on {{ f.leavingDate | date: 'd MMM yyyy' }}. Correcting it lets them sign in again.</p>
            <label class="field wide"><span>Reason (at least 10 characters)</span><textarea data-fix-reason rows="2" maxlength="500" [value]="fixReason()" (input)="fixReason.set($any($event.target).value)"></textarea></label>
            <app-button data-correct-btn [disabled]="busy() || fixReason().trim().length < 10" (click)="correct(f)">Correct this leaving</app-button>
          } @else {
            <p class="issue" role="alert">This leaving cannot be corrected:</p>
            <ul class="issue" data-fix-blockers>@for (b of f.blockers; track b) { <li>{{ b }}</li> }</ul>
          }
        </div>
      }
    </app-card>

    <app-card title="Students who left">
      @if (loading()) { <p role="status">Loading…</p> }
      @else if (failed()) { <p class="issue" role="alert" data-failed>The list could not be loaded. Please try again.</p> }
      @else {
        <div class="row">
          <label class="field"><span>Year</span>
            <select data-year (change)="year.set($any($event.target).value)">
              <option value="">All years</option>
              @for (y of years(); track y) { <option [value]="y" [selected]="y === year()">{{ y }}</option> }
            </select></label>
        </div>
        @if (shown().length === 0) { <p data-empty>Nobody has been recorded as left.</p> }
        @else {
          <div class="scroll">
            <table class="rows" data-rows>
              <thead><tr><th>Student</th><th>Class</th><th>What</th><th>Date</th><th>Reason</th><th>Recorded by</th></tr></thead>
              <tbody>
                @for (l of shown(); track l.studentId + l.yearName) {
                  <tr [attr.data-row]="l.studentId">
                    <td>{{ l.studentName }}<small>{{ l.studentId }} · {{ l.admissionNumber }}</small></td>
                    <td>{{ l.className }} {{ l.sectionName }}<small>{{ l.yearName }}</small></td>
                    <td>{{ l.outcome === 'LEFT' ? 'Left' : 'Transferred out' }}@if (l.overridden) { <small>Dues overridden</small> }</td>
                    <td>{{ l.leavingDate ? (l.leavingDate | date: 'd MMM yyyy') : '-' }}</td>
                    <td>{{ l.reason ?? '-' }}</td>
                    <td>{{ l.recordedBy ?? 'Promotion run' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </app-card>
  `,
  styles: `
    .hint { margin: 0 0 var(--space-3); font-size: var(--text-sm); color: var(--color-text-muted); }
    .row { display: flex; flex-wrap: wrap; gap: var(--space-4); align-items: flex-end; margin-bottom: var(--space-3); }
    .field { display: flex; flex-direction: column; gap: var(--space-1); font-size: var(--text-sm); min-width: 200px; }
    .wide { min-width: 100%; textarea { width: 100%; } }
    .check { display: flex; gap: var(--space-2); align-items: center; font-size: var(--text-sm); margin: var(--space-2) 0; }
    .issue { color: #991b1b; margin: var(--space-2) 0; }
    .scroll { overflow-x: auto; }
    .rows { width: 100%; border-collapse: collapse; font-size: var(--text-sm); }
    .rows th, .rows td { text-align: left; padding: var(--space-2) var(--space-3); border-bottom: 1px solid var(--color-border); vertical-align: top; }
    small { display: block; color: var(--color-text-muted); }
  `,
})
export class LeavingComponent implements OnInit {
  private readonly api = inject(StudentLeavingService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly studentId = signal('');
  protected readonly date = signal('');
  protected readonly type = signal<LeavingType>('LEFT');
  protected readonly reason = signal('');
  protected readonly overrideDues = signal(false);
  protected readonly overrideReason = signal('');
  protected readonly result = signal<LeavingCheck | null>(null);
  protected readonly checkFailed = signal(false);
  protected readonly busy = signal(false);

  protected readonly fixId = signal('');
  protected readonly fixReason = signal('');
  protected readonly fixResult = signal<LeavingCorrectionCheck | null>(null);

  protected readonly leavers = signal<Leaver[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly year = signal('');
  protected readonly years = computed(() => [...new Set(this.leavers().map((l) => l.yearName))]);
  protected readonly shown = computed(() => (this.year() ? this.leavers().filter((l) => l.yearName === this.year()) : this.leavers()));

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.leavers().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.leavers.set(l), error: () => this.failed.set(true) });
  }

  protected resetCheck(): void { this.result.set(null); this.checkFailed.set(false); this.overrideDues.set(false); this.overrideReason.set(''); }

  protected check(): void {
    if (!this.studentId().trim() || this.busy()) return;
    this.busy.set(true);
    this.resetCheck();
    this.api.check(this.studentId().trim(), this.date() || undefined).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.result.set(c), error: () => this.checkFailed.set(true) });
  }

  protected async record(c: LeavingCheck): Promise<void> {
    if (this.busy() || !this.reason().trim()) return;
    const ok = await this.confirmDialog.confirm({ title: 'Record this leaving?', message: `${c.studentName} will leave every live class list and their login will stop working at once.`, confirmText: 'Record', variant: 'danger' });
    if (!ok) return;
    this.busy.set(true);
    this.api
      .record({ studentId: c.studentId, type: this.type(), leavingDate: this.date() || undefined, reason: this.reason().trim(), confirm: true, overrideDues: c.requiresOverride ? this.overrideDues() : undefined,
                overrideReason: c.requiresOverride ? this.overrideReason().trim() : undefined })
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => { this.toast.success(`${r.studentName} is recorded as ${r.outcome === 'LEFT' ? 'left' : 'transferred out'}.`); this.resetCheck(); this.reason.set(''); this.studentId.set(''); this.load(); },
        error: () => undefined,   // the server's plain reason is toasted; nothing was recorded
      });
  }

  protected checkFix(): void {
    if (!this.fixId().trim() || this.busy()) return;
    this.busy.set(true);
    this.fixResult.set(null);
    this.api.checkCorrection(this.fixId().trim()).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (f) => this.fixResult.set(f), error: () => undefined });
  }

  protected async correct(f: LeavingCorrectionCheck): Promise<void> {
    if (this.busy() || this.fixReason().trim().length < 10) return;
    const ok = await this.confirmDialog.confirm({ title: 'Correct this leaving?', message: `${f.studentName} goes back on the class lists and can sign in again. The correction is recorded.`, confirmText: 'Correct it', variant: 'danger' });
    if (!ok) return;
    this.busy.set(true);
    this.api.correct(f.studentId, this.fixReason().trim()).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.toast.success('The leaving was corrected.'); this.fixResult.set(null); this.fixReason.set(''); this.fixId.set(''); this.load(); },
      error: () => undefined,
    });
  }
}
