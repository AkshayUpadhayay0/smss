import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent } from '../../../../shared/components';
import { DECISIONS, YearStudent, YearStudents } from '../../models/marks.model';
import { MarksService } from '../../services/marks.service';

/** Promotion for the year: the decision, "promoted to" text and the date the new session begins. A decision field only; the Student Promotion module will use it later. */
@Component({
  selector: 'app-class-promotion-tab',
  imports: [ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    @if (loading()) { <p class="note" role="status">Loading…</p> }
    @else if (data(); as d) {
      @if (d.readOnlyReason) { <p class="note" role="status">{{ d.readOnlyReason }}</p> }
      <div class="table-wrap">
        <table class="dense" aria-label="Promotion">
          <thead><tr><th>Roll</th><th>Student</th><th>Decision</th><th>Promoted to</th><th>New session begins</th></tr></thead>
          <tbody>
            @for (s of rows(); track s.studentId) {
              <tr>
                <td>{{ s.rollNumber || '—' }}</td><td>{{ s.fullName }}</td>
                <td>
                  <select [disabled]="!d.canEdit" (change)="patch(s.studentId, { promotionDecision: $any($event.target).value || null })" [attr.aria-label]="s.fullName + ' decision'">
                    <option value="" [selected]="!s.promotionDecision">Not decided</option>
                    @for (o of decisions; track o.value) { <option [value]="o.value" [selected]="s.promotionDecision === o.value">{{ o.label }}</option> }
                  </select>
                </td>
                <td><input type="text" maxlength="60" [value]="s.promotedTo ?? ''" [disabled]="!d.canEdit" (input)="patch(s.studentId, { promotedTo: $any($event.target).value })" [attr.aria-label]="s.fullName + ' promoted to'" /></td>
                <td><input type="date" [value]="s.newSessionDate ?? ''" [disabled]="!d.canEdit" (change)="patch(s.studentId, { newSessionDate: $any($event.target).value || null })" [attr.aria-label]="s.fullName + ' new session date'" /></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      @if (d.canEdit) { <div class="row-actions" style="margin-top: var(--space-4)"><app-button [disabled]="saving() || !dirty()" (click)="save()">{{ saving() ? 'Saving…' : 'Save promotion' }}</app-button></div> }
    }
  `,
})
export class ClassPromotionTabComponent implements OnChanges {
  readonly schemeId = input.required<number>();
  readonly sectionId = input.required<number>();
  protected readonly decisions = DECISIONS;
  private readonly service = inject(MarksService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly data = signal<YearStudents | null>(null);
  protected readonly rows = signal<YearStudent[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly dirty = signal(false);

  ngOnChanges(): void {
    this.loading.set(true); this.dirty.set(false);
    this.service.yearStudents(this.schemeId(), this.sectionId()).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (d) => { this.data.set(d); this.rows.set(d.students); }, error: () => this.data.set(null) });
  }
  protected patch(id: string, change: Partial<YearStudent>): void { this.rows.update((r) => r.map((s) => (s.studentId === id ? { ...s, ...change } : s))); this.dirty.set(true); }
  protected save(): void {
    const d = this.data(); if (!d) return;
    this.saving.set(true);
    this.service.saveYearStudents(d.schemeId, d.sectionId, this.rows()).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (fresh) => { this.data.set(fresh); this.rows.set(fresh.students); this.dirty.set(false); this.toast.success('Saved.'); },
      error: () => undefined,
    });
  }
}
