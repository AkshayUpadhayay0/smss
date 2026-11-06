import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent } from '../../../../shared/components';
import { ClassSubjectService } from '../../../school-setup/services/class-subject.service';
import { SaveSchemeSubjectRequest, Scheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

interface Row { classId: number; subjectId: number; name: string; on: boolean; order: string; label: string; inTotal: boolean }
interface Group { classId: number; className: string; rows: Row[] }

/**
 * The subjects of each class in this scheme, taken from Class-Subject Mapping. A subject that is not counted in the total is still printed on the card (a hobby subject),
 * but is left out of the total, the percentage, the rank and the pass rule.
 */
@Component({
  selector: 'app-scheme-subjects',
  imports: [ButtonComponent, CardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    @if (loading()) {
      <app-card><p class="note" role="status">Loading the subjects of each class…</p></app-card>
    } @else if (groups().length === 0) {
      <app-card><p class="note">Add classes to this scheme first.</p></app-card>
    } @else {
      <div class="stack">
        @for (g of groups(); track g.classId; let gi = $index) {
          <app-card [title]="g.className">
            @if (g.rows.length === 0) {
              <p class="note">No subjects are mapped to {{ g.className }}. Add them under Class-Subject Mapping, then reopen this page.</p>
            } @else {
              <div class="table-wrap">
                <table class="dense" [attr.aria-label]="'Subjects of ' + g.className">
                  <thead><tr><th>In this scheme</th><th>Subject</th><th>Order</th><th>Printed as</th><th>Counts in the total</th></tr></thead>
                  <tbody>
                    @for (r of g.rows; track r.subjectId; let ri = $index) {
                      <tr>
                        <td><input type="checkbox" [checked]="r.on" [disabled]="!canEdit()" (change)="set(gi, ri, 'on', $any($event.target).checked)" [attr.aria-label]="'Include ' + r.name" /></td>
                        <td>{{ r.name }}</td>
                        <td><input type="number" min="0" max="999" [value]="r.order" [disabled]="!canEdit() || !r.on" (input)="set(gi, ri, 'order', $any($event.target).value)" [attr.aria-label]="'Order of ' + r.name" /></td>
                        <td><input type="text" maxlength="60" [value]="r.label" [placeholder]="r.name" [disabled]="!canEdit() || !r.on" (input)="set(gi, ri, 'label', $any($event.target).value)" [attr.aria-label]="'Printed name of ' + r.name" /></td>
                        <td><input type="checkbox" [checked]="r.inTotal" [disabled]="!canEdit() || !r.on" (change)="set(gi, ri, 'inTotal', $any($event.target).checked)" [attr.aria-label]="r.name + ' counts in the total'" /></td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </app-card>
        }
        @if (canEdit()) { <div class="row-actions"><app-button [disabled]="saving()" (click)="save()">{{ saving() ? 'Saving…' : 'Save subjects' }}</app-button></div> }
      </div>
    }
  `,
})
export class SchemeSubjectsComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly mapping = inject(ClassSubjectService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly scheme = input.required<Scheme>();
  readonly canEdit = input(false);
  readonly saved = output<Scheme>();

  protected readonly groups = signal<Group[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly count = computed(() => this.groups().reduce((a, g) => a + g.rows.filter((r) => r.on).length, 0));

  ngOnInit(): void {
    const s = this.scheme();
    if (s.classes.length === 0) { this.loading.set(false); return; }
    forkJoin(s.classes.map((c) => this.mapping.listByClass(c.classId)))
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (all) => this.groups.set(s.classes.map((c, i) => {
          const have = new Map(s.subjects.filter((x) => x.classId === c.classId).map((x) => [x.subjectId, x]));
          const mapped = all[i].map((m) => ({ id: m.subjectId, name: m.subjectName }));
          // a subject already in the scheme that is no longer mapped stays visible (and ticked), so nothing silently disappears
          for (const x of have.values()) if (!mapped.some((m) => m.id === x.subjectId)) mapped.push({ id: x.subjectId, name: `${x.subjectName} (no longer mapped)` });
          return {
            classId: c.classId, className: c.className,
            rows: mapped.map((m, k) => {
              const h = have.get(m.id);
              return { classId: c.classId, subjectId: m.id, name: m.name, on: !!h || s.subjects.length === 0, order: String(h?.displayOrder ?? k + 1), label: h?.labelOverride ?? '', inTotal: h?.includeInTotal ?? true };
            }),
          };
        })),
        error: () => undefined,
      });
  }

  protected set(g: number, r: number, f: keyof Row, v: string | boolean): void {
    this.groups.update((gs) => gs.map((x, i) => (i === g ? { ...x, rows: x.rows.map((row, j) => (j === r ? { ...row, [f]: v } : row)) } : x)));
  }

  protected save(): void {
    if (this.saving()) return;
    const body: SaveSchemeSubjectRequest[] = this.groups().flatMap((g) => g.rows.filter((r) => r.on).map((r) => ({
      classId: r.classId, subjectId: r.subjectId, displayOrder: Math.max(0, Math.round(Number(r.order) || 0)), labelOverride: r.label.trim() || null, includeInTotal: r.inTotal,
    })));
    this.saving.set(true);
    this.service.saveSubjects(this.scheme().schemeId, body).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (s) => { this.toast.success('Subjects saved. Tick them in the subject matrix to say which exams cover them.'); this.saved.emit(s); },
      error: () => undefined,
    });
  }
}
