import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent } from '../../../../shared/components';
import { Scheme } from '../../models/examinations.model';
import { ExaminationsService } from '../../services/examinations.service';

/** Skills a subject is graded on (Reading, Writing, Oral ...), for grade-only skill grids. One line per subject, skills separated by commas, in the order they print. */
@Component({
  selector: 'app-scheme-skills',
  imports: [ButtonComponent, CardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrls: ['../../styles/exam-common.scss'],
  template: `
    <app-card title="Skills per subject">
      @if (scheme().subjects.length === 0) {
        <p class="note">Add the subjects first.</p>
      } @else {
        <p class="note">Leave a subject blank to grade it as a whole. Up to {{ max }} skills each.</p>
        <div class="table-wrap">
          <table class="dense" aria-label="Skills per subject">
            <thead><tr><th>Class</th><th>Subject</th><th>Skills (comma separated)</th></tr></thead>
            <tbody>
              @for (s of scheme().subjects; track s.schemeSubjectId) {
                <tr>
                  <td>{{ s.className }}</td>
                  <td>{{ s.labelOverride || s.subjectName }}</td>
                  <td><input type="text" class="wide" [value]="text()[s.schemeSubjectId]" [disabled]="!canEdit()" (input)="set(s.schemeSubjectId, $any($event.target).value)" placeholder="Reading, Writing, Oral" [attr.aria-label]="'Skills of ' + s.subjectName + ' in ' + s.className" /></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (problem(); as p) { <p class="error-text" role="alert">{{ p }}</p> }
        @if (canEdit()) { <div class="row-actions" style="margin-top: var(--space-4)"><app-button [disabled]="saving() || !!problem()" (click)="save()">{{ saving() ? 'Saving…' : 'Save skills' }}</app-button></div> }
      }
    </app-card>
  `,
  styles: `.wide { width: 100%; min-width: 260px; }`,
})
export class SchemeSkillsComponent implements OnInit {
  private readonly service = inject(ExaminationsService);
  private readonly toast = inject(ToastService);

  readonly scheme = input.required<Scheme>();
  readonly canEdit = input(false);
  readonly saved = output<Scheme>();

  protected readonly max = 10;
  protected readonly text = signal<Record<number, string>>({});
  protected readonly saving = signal(false);

  ngOnInit(): void { this.text.set(Object.fromEntries(this.scheme().subjects.map((s) => [s.schemeSubjectId, s.skills.map((k) => k.name).join(', ')]))); }

  private parse(v: string): string[] { return v.split(',').map((x) => x.trim()).filter(Boolean); }

  protected readonly problem = () => {
    for (const [id, v] of Object.entries(this.text())) {
      const list = this.parse(v);
      const name = this.scheme().subjects.find((s) => s.schemeSubjectId === Number(id))?.subjectName ?? 'A subject';
      if (list.length > this.max) return `${name}: at most ${this.max} skills.`;
      if (new Set(list.map((x) => x.toLowerCase())).size !== list.length) return `${name}: skill names must be different.`;
      if (list.some((x) => x.length > 60)) return `${name}: a skill name can be at most 60 characters.`;
    }
    return null;
  };

  protected set(id: number, v: string): void { this.text.update((t) => ({ ...t, [id]: v })); }

  protected save(): void {
    if (this.saving() || this.problem()) return;
    this.saving.set(true);
    this.service.saveSkills(this.scheme().schemeId, this.scheme().subjects.map((s) => ({ schemeSubjectId: s.schemeSubjectId, skills: this.parse(this.text()[s.schemeSubjectId] ?? '') })))
      .pipe(finalize(() => this.saving.set(false))).subscribe({
        next: (s) => { this.toast.success('Skills saved.'); this.saved.emit(s); },
        error: () => undefined,
      });
  }
}
