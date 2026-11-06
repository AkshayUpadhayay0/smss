import { ChangeDetectionStrategy, Component, DestroyRef, OnChanges, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent } from '../../../../shared/components';
import { ActivityGrades } from '../../models/marks.model';
import { MarksService } from '../../services/marks.service';

/** Co-scholastic and discipline grades for one term: a grade per student per area, from each area's own rating scale. */
@Component({
  selector: 'app-class-activity-tab',
  imports: [ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    @if (loading()) { <p class="note" role="status">Loading…</p> }
    @else if (data(); as d) {
      @if (d.readOnlyReason) { <p class="note" role="status">{{ d.readOnlyReason }}</p> }
      @if (d.areas.length === 0) { <p class="note">No activity areas are set up yet. The School Admin adds them under Examinations, Activity Areas.</p> }
      @else {
        <div class="table-wrap">
          <table class="dense" aria-label="Activity grades">
            <thead><tr><th>Roll</th><th>Student</th>@for (a of d.areas; track a.areaId) { <th>{{ a.name }}</th> }</tr></thead>
            <tbody>
              @for (s of d.students; track s.studentId) {
                <tr>
                  <td>{{ s.rollNumber || '—' }}</td><td>{{ s.fullName }}</td>
                  @for (a of d.areas; track a.areaId) {
                    <td>
                      <select [disabled]="!d.canEdit" (change)="set(s.studentId, a.areaId, $any($event.target).value)" [attr.aria-label]="s.fullName + ' ' + a.name">
                        <option value="" [selected]="!grade(s.studentId, a.areaId)">—</option>
                        @for (o of a.options; track o.code) { <option [value]="o.code" [selected]="grade(s.studentId, a.areaId) === o.code">{{ o.code }}{{ o.label && o.label !== o.code ? ' · ' + o.label : '' }}</option> }
                      </select>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (d.canEdit) { <div class="row-actions" style="margin-top: var(--space-4)"><app-button [disabled]="saving() || !dirty()" (click)="save()">{{ saving() ? 'Saving…' : 'Save activity grades' }}</app-button></div> }
      }
    }
  `,
})
export class ClassActivityTabComponent implements OnChanges {
  readonly termId = input.required<number>();
  readonly sectionId = input.required<number>();
  private readonly service = inject(MarksService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly data = signal<ActivityGrades | null>(null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly dirty = signal(false);

  ngOnChanges(): void { this.load(); }
  private load(): void {
    this.loading.set(true); this.dirty.set(false);
    this.service.activity(this.termId(), this.sectionId()).pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({ next: (d) => this.data.set(d), error: () => this.data.set(null) });
  }
  protected grade(studentId: string, areaId: number): string { return this.data()?.students.find((s) => s.studentId === studentId)?.grades[String(areaId)] ?? ''; }
  protected set(studentId: string, areaId: number, code: string): void {
    this.data.update((d) => d && { ...d, students: d.students.map((s) => (s.studentId === studentId ? { ...s, grades: { ...s.grades, [String(areaId)]: code } } : s)) });
    this.dirty.set(true);
  }
  protected save(): void {
    const d = this.data(); if (!d) return;
    this.saving.set(true);
    this.service.saveActivity(d.termId, d.sectionId, d.students, d.areas.map((a) => a.areaId)).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (fresh) => { this.data.set(fresh); this.dirty.set(false); this.toast.success('Activity grades saved.'); },
      error: () => undefined,
    });
  }
}
