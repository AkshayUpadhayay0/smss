import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent } from '../../../../shared/components';
import { SchoolClass } from '../../../school-setup/models/class.model';
import { ClassService } from '../../../school-setup/services/class.service';
import { Scheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/** Which classes this scheme is for. A class can be in one scheme per academic year (the server refuses a clash and says which scheme has it). */
@Component({
  selector: 'app-scheme-classes',
  imports: [ButtonComponent, CardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-card title="Classes in this scheme">
      @if (loading()) {
        <p class="note" role="status">Loading classes…</p>
      } @else {
        <ul class="list">
          @for (c of classes(); track c.classId) {
            <li><label class="check"><input type="checkbox" [checked]="picked().has(c.classId)" [disabled]="!canEdit()" (change)="toggle(c.classId, $any($event.target).checked)" /> {{ c.className }}</label></li>
          }
        </ul>
        @if (classes().length === 0) { <p class="note">There are no classes yet.</p> }
        @if (canEdit()) {
          <p class="note">Taking a class out also removes its subjects and subject matrix from this scheme.</p>
          <div class="row-actions"><app-button [disabled]="saving() || picked().size === 0" (click)="save()">{{ saving() ? 'Saving…' : 'Save classes' }}</app-button></div>
        }
      }
    </app-card>
  `,
  styles: `.list { list-style: none; margin: 0 0 var(--space-4); padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: var(--space-3); }`,
})
export class SchemeClassesComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly classesApi = inject(ClassService);
  private readonly confirm = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly scheme = input.required<Scheme>();
  readonly canEdit = input(false);
  readonly saved = output<Scheme>();

  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly picked = signal<ReadonlySet<number>>(new Set());

  ngOnInit(): void {
    this.picked.set(new Set(this.scheme().classes.map((c) => c.classId)));
    this.classesApi.list().pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (l) => this.classes.set(l.filter((c) => (c.statusName ?? 'Active') === 'Active' || this.picked().has(c.classId)).sort((a, b) => a.sequenceOrder - b.sequenceOrder)),
      error: () => undefined,
    });
  }

  protected toggle(id: number, on: boolean): void { this.picked.update((s) => { const n = new Set(s); if (on) n.add(id); else n.delete(id); return n; }); }

  protected async save(): Promise<void> {
    const removing = this.scheme().classes.filter((c) => !this.picked().has(c.classId));
    if (removing.length > 0 && !(await this.confirm.confirm({ title: 'Take these classes out?', message: `${removing.map((c) => c.className).join(', ')} will leave this scheme, with their subjects and subject matrix.`, confirmText: 'Take out', variant: 'danger' }))) return;
    this.saving.set(true);
    this.service.saveClasses(this.scheme().schemeId, [...this.picked()]).pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (s) => { this.toast.success('Classes saved.'); this.saved.emit(s); },
      error: () => undefined, // e.g. "Class 3 is already in <scheme> for this academic year"
    });
  }
}
