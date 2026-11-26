import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { SectionHistoryComponent } from '../../components/section-history/section-history.component';
import { SectionChangeCheck, SectionTarget } from '../../models/student-leaving.model';
import { StudentLeavingService } from '../../services/student-leaving.service';

/**
 * Change a student's SECTION in the middle of the year (School Admin and Principal): same class, same year. Their enrolment, fees, transport and past attendance and marks stay as they are; only the section
 * and the roll number change, and the change is kept in the student's history. The server decides everything (the sections offered, the capacity, the roll number, what blocks it).
 */
@Component({
  selector: 'app-section-change',
  imports: [ButtonComponent, CardComponent, PageHeaderComponent, SectionHistoryComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="Change section" subtitle="Move a student to another section of the same class, this year" icon="graduation-cap" [breadcrumbs]="[{ label: 'Students', link: '/students' }, { label: 'Change section' }]" />
    <app-card title="Move a student">
      <p class="hint">The class, the fees and the past attendance and marks do not change. Enter the student id and check.</p>
      <div class="row">
        <label class="field"><span>Student id</span><input data-student [value]="studentId()" (input)="studentId.set($any($event.target).value); reset()" autocomplete="off" /></label>
        <app-button data-check-btn variant="secondary" [disabled]="!studentId().trim() || busy()" (click)="check()">Check</app-button>
      </div>
      @if (failed()) { <p class="issue" role="alert" data-failed>That could not be checked. Please try again.</p> }
      @if (result(); as c) {
        <div data-check>
          <p><strong>{{ c.studentName }}</strong> ({{ c.studentId }}) · {{ c.className }} · Section {{ c.currentSectionName }} · roll {{ c.currentRoll ?? '-' }} · {{ c.yearName }}</p>
          @if (c.blockers.length) { <ul class="issue" data-blockers>@for (b of c.blockers; track b) { <li>{{ b }}</li> }</ul> }
          @if (c.canChange) {
            <div class="row">
              <label class="field"><span>New section</span>
                <select data-target (change)="pick($any($event.target).value)">
                  <option value="">Choose…</option>
                  @for (t of c.targets; track t.sectionId) {
                    <option [value]="t.sectionId" [disabled]="t.isFull">Section {{ t.sectionName }} ({{ t.enrolled }}{{ t.maxStrength ? ' of ' + t.maxStrength : '' }}{{ t.isFull ? ', full' : '' }})</option>
                  }
                </select></label>
              <label class="field"><span>Roll number (optional)</span><input data-roll maxlength="30" [value]="roll()" (input)="roll.set($any($event.target).value)" [attr.placeholder]="picked() ? 'Next free: ' + picked()!.nextRoll : ''" /></label>
              <label class="field"><span>Effective date</span><input data-date type="date" [value]="date()" (input)="date.set($any($event.target).value)" /></label>
            </div>
            <label class="field wide"><span>Reason (at least 10 characters)</span><textarea data-reason rows="2" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
            <app-button data-change-btn [disabled]="busy() || !picked() || reason().trim().length < 10" (click)="change(c)">{{ busy() ? 'Moving…' : 'Change section' }}</app-button>
          }
          <app-section-history [studentId]="c.studentId" [refreshKey]="historyKey()" />
        </div>
      }
    </app-card>
  `,
  styles: `
    .hint { margin: 0 0 var(--space-3); font-size: var(--text-sm); color: var(--color-text-muted); }
    .row { display: flex; flex-wrap: wrap; gap: var(--space-4); align-items: flex-end; margin-bottom: var(--space-3); }
    .field { display: flex; flex-direction: column; gap: var(--space-1); font-size: var(--text-sm); min-width: 200px; }
    .wide { min-width: 100%; textarea { width: 100%; } }
    .issue { color: #991b1b; margin: var(--space-2) 0; }
  `,
})
export class SectionChangeComponent {
  private readonly api = inject(StudentLeavingService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly studentId = signal('');
  protected readonly result = signal<SectionChangeCheck | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly picked = signal<SectionTarget | null>(null);
  protected readonly roll = signal('');
  protected readonly date = signal('');
  protected readonly reason = signal('');
  protected readonly historyKey = signal(0);

  protected reset(): void { this.result.set(null); this.failed.set(false); this.picked.set(null); this.roll.set(''); }

  protected pick(id: string): void { this.picked.set(this.result()?.targets.find((t) => String(t.sectionId) === id) ?? null); }

  protected check(): void {
    if (!this.studentId().trim() || this.busy()) return;
    this.busy.set(true);
    this.reset();
    this.api.sectionCheck(this.studentId().trim()).pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (c) => this.result.set(c), error: () => this.failed.set(true) });
  }

  protected async change(c: SectionChangeCheck): Promise<void> {
    const t = this.picked();
    if (!t || this.busy() || this.reason().trim().length < 10) return;
    const ok = await this.confirmDialog.confirm({ title: 'Change this student\'s section?', message: `${c.studentName} moves from Section ${c.currentSectionName} to Section ${t.sectionName}. The change is kept in their history.`, confirmText: 'Move', variant: 'primary' });
    if (!ok) return;
    this.busy.set(true);
    this.api
      .changeSection({ studentId: c.studentId, toSectionId: t.sectionId, reason: this.reason().trim(), confirm: true, rollNumber: this.roll().trim() || undefined, effectiveDate: this.date() || undefined })
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => { this.toast.success(`${r.studentName} is now in Section ${r.toSection} (roll ${r.toRoll ?? '-'}).`); this.reason.set(''); this.date.set(''); this.picked.set(null); this.roll.set(''); this.busy.set(false); this.historyKey.update((k) => k + 1); this.check(); },
        error: () => undefined,   // the server's plain reason is toasted; nothing was changed
      });
  }
}
