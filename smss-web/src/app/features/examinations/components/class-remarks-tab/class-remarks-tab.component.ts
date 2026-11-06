import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent } from '../../../../shared/components';
import { TermAttendance, TermStudent, TermStudents } from '../../models/marks.model';
import { MarksService } from '../../services/marks.service';

/** Remarks, conduct, rank text and attendance for one term. Attendance is read from the Attendance module (AUTO) or typed here (MANUAL), per the scheme. */
@Component({
  selector: 'app-class-remarks-tab',
  imports: [ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    @if (loading()) { <p class="note" role="status">Loading…</p> }
    @else if (data(); as d) {
      @if (d.readOnlyReason) { <p class="note" role="status">{{ d.readOnlyReason }}</p> }
      @if (att(); as a) {
        <div class="bar">
          <span class="note"><strong>Attendance</strong> · {{ a.source === 'AUTO' ? 'read from the Attendance module' : 'typed in by the class teacher' }} · {{ a.from }} to {{ a.to }}@if (a.source === 'AUTO') { · {{ a.workingDays }} working days so far }</span>
        </div>
        @if (a.warning) { <ul class="warnings" role="status"><li>{{ a.warning }}</li></ul> }
      }
      <div class="table-wrap">
        <table class="dense" aria-label="Remarks and attendance">
          <thead>
            <tr><th>Roll</th><th>Student</th>
              @if (d.attendanceSource === 'MANUAL') { <th>Working days</th><th>Present days</th> } @else { <th class="num">Present</th><th class="num">Marked days</th><th class="num">%</th> }
              <th>Conduct</th>@if (d.evaluationMode === 'GRADES') { <th>Rank</th> }<th>Remark</th></tr>
          </thead>
          <tbody>
            @for (s of rows(); track s.studentId) {
              <tr>
                <td>{{ s.rollNumber || '—' }}</td><td>{{ s.fullName }}</td>
                @if (d.attendanceSource === 'MANUAL') {
                  <td><input type="number" min="0" max="366" [value]="s.manualWorkingDays ?? ''" [disabled]="!d.canEdit" (input)="num(s.studentId, 'manualWorkingDays', $any($event.target).value)" [attr.aria-label]="s.fullName + ' working days'" /></td>
                  <td><input type="number" min="0" max="366" [value]="s.manualPresentDays ?? ''" [disabled]="!d.canEdit" (input)="num(s.studentId, 'manualPresentDays', $any($event.target).value)" [attr.aria-label]="s.fullName + ' present days'" /></td>
                } @else {
                  <td class="num">{{ auto(s.studentId)?.presentDays ?? '—' }}</td><td class="num">{{ auto(s.studentId)?.workingDays ?? '—' }}</td><td class="num">{{ auto(s.studentId)?.percentage ?? '—' }}</td>
                }
                <td><input type="text" maxlength="60" [value]="s.conduct ?? ''" [disabled]="!d.canEdit" (input)="text(s.studentId, 'conduct', $any($event.target).value)" [attr.aria-label]="s.fullName + ' conduct'" /></td>
                @if (d.evaluationMode === 'GRADES') { <td><input type="text" maxlength="30" [value]="s.rankText ?? ''" [disabled]="!d.canEdit" (input)="text(s.studentId, 'rankText', $any($event.target).value)" [attr.aria-label]="s.fullName + ' rank'" /></td> }
                <td><input type="text" maxlength="500" class="wide" [value]="s.remark ?? ''" [disabled]="!d.canEdit" (input)="text(s.studentId, 'remark', $any($event.target).value)" [attr.aria-label]="s.fullName + ' remark'" /></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (d.canEdit) { <div class="row-actions" style="margin-top: var(--space-4)"><app-button [disabled]="saving() || !dirty()" (click)="save()">{{ saving() ? 'Saving…' : 'Save remarks and attendance' }}</app-button></div> }
    }
  `,
  styles: `input.wide { min-width: 220px; }`,
})
export class ClassRemarksTabComponent implements OnChanges {
  readonly termId = input.required<number>();
  readonly sectionId = input.required<number>();
  private readonly service = inject(MarksService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly data = signal<TermStudents | null>(null);
  protected readonly att = signal<TermAttendance | null>(null);
  protected readonly rows = signal<TermStudent[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly dirty = signal(false);
  private readonly autoMap = computed(() => new Map((this.att()?.students ?? []).map((s) => [s.studentId, s])));

  ngOnChanges(): void { this.load(); }
  private load(): void {
    this.loading.set(true); this.dirty.set(false);
    forkJoin({ t: this.service.termStudents(this.termId(), this.sectionId()), a: this.service.attendance(this.termId(), this.sectionId()) })
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: ({ t, a }) => { this.data.set(t); this.att.set(a); this.rows.set(t.students); }, error: () => { this.data.set(null); this.att.set(null); } });
  }
  protected auto(id: string) { return this.autoMap().get(id); }
  private patch(id: string, change: Partial<TermStudent>): void { this.rows.update((r) => r.map((s) => (s.studentId === id ? { ...s, ...change } : s))); this.dirty.set(true); }
  protected text(id: string, field: 'conduct' | 'rankText' | 'remark', v: string): void { this.patch(id, { [field]: v } as Partial<TermStudent>); }
  protected num(id: string, field: 'manualWorkingDays' | 'manualPresentDays', v: string): void { this.patch(id, { [field]: v === '' ? null : Number(v) } as Partial<TermStudent>); }
  protected save(): void {
    const d = this.data(); if (!d) return;
    this.saving.set(true);
    this.service.saveTermStudents(d.termId, d.sectionId, this.rows()).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (fresh) => { this.data.set(fresh); this.rows.set(fresh.students); this.dirty.set(false); this.toast.success('Saved.'); this.service.attendance(d.termId, d.sectionId).subscribe((a) => this.att.set(a)); },
      error: () => undefined,
    });
  }
}
