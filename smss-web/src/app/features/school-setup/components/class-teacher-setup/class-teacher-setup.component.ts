import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent } from '../../../../shared/components';
import { ClassTeacherAssignment, ClassTeacherList, ClassTeacherSection, EligibleTeacher } from '../../models/academic-setup.model';
import { ClassTeacherApiService } from '../../services/class-teacher-history.service';

/** "2026-11-03" -> "03 Nov 2026" (no time zone maths: the value is a calendar day). */
export function dayLabel(d: string | null | undefined): string {
  if (!d) return '';
  const [y, m, day] = d.split('-').map(Number);
  return `${String(day).padStart(2, '0')} ${['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][m - 1]} ${y}`;
}

/** The OTHER sections (as "Class - Section") the teacher is already the current class teacher of. */
export function otherSectionsOf(list: ClassTeacherSection[], employeeId: string, exceptSectionId: number): string[] {
  return list.filter((s) => s.employeeId === employeeId && s.sectionId !== exceptSectionId).map((s) => `${s.className} - ${s.sectionName}`);
}

/** One line of the history drawer: who, from, to, and why. */
export function historyLine(a: ClassTeacherAssignment): string {
  const to = a.effectiveTo ? dayLabel(a.effectiveTo) : 'now';
  return `${dayLabel(a.effectiveFrom)} to ${to}`;
}

type Mode = 'assign' | 'remove' | 'history';

/**
 * Academic Setup, Class teachers (S5): the current class teacher of each section for the chosen year, with Assign / Change (a reason is kept), Remove and a history
 * drawer. Nothing is ever deleted or edited in place: a change closes the old row and opens a new one. A teacher who already has another section needs a confirm.
 */
@Component({
  selector: 'app-class-teacher-setup',
  imports: [ButtonComponent, IconComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './class-teacher-setup.component.scss',
  templateUrl: './class-teacher-setup.component.html',
})
export class ClassTeacherSetupComponent {
  private readonly api = inject(ClassTeacherApiService);
  private readonly toast = inject(ToastService);

  readonly yearId = input.required<number>();
  readonly readOnly = input(false);

  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly data = signal<ClassTeacherList | null>(null);
  protected readonly teachers = signal<EligibleTeacher[]>([]);
  protected readonly teachersLoading = signal(false);
  protected readonly teachersFailed = signal(false);
  protected readonly busy = signal(false);

  // the dialog
  protected readonly mode = signal<Mode | null>(null);
  protected readonly target = signal<ClassTeacherSection | null>(null);
  protected readonly pick = signal('');
  protected readonly reason = signal('');
  protected readonly confirmMultiple = signal(false);
  protected readonly history = signal<ClassTeacherAssignment[]>([]);
  protected readonly historyLoading = signal(false);

  protected readonly locked = computed(() => this.readOnly());
  protected readonly sections = computed(() => this.data()?.sections ?? []);
  protected readonly others = computed(() => {
    const t = this.target();
    const p = this.pick();
    return t && p ? otherSectionsOf(this.sections(), p, t.sectionId) : [];
  });
  protected readonly canSave = computed(() => {
    if (this.busy()) return false;
    if (this.mode() === 'remove') return true;
    return this.pick() !== '' && (this.others().length === 0 || this.confirmMultiple());
  });

  protected readonly day = dayLabel;
  protected readonly range = historyLine;

  constructor() {
    effect(() => {
      const id = this.yearId();
      untracked(() => this.load(id));
    });
  }

  protected load(id: number): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api.list(id).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (d) => this.data.set(d),
      error: () => { this.data.set(null); this.failed.set(true); },
    });
    if (this.teachers().length === 0 && !this.teachersLoading()) this.loadTeachers();
  }

  /** The teachers who can be picked. Reloaded each time the Assign dialog opens (a teacher or login added since is then seen); a failure is shown, never an empty list. */
  protected loadTeachers(): void {
    this.teachersLoading.set(true);
    this.teachersFailed.set(false);
    this.api.eligible().pipe(finalize(() => this.teachersLoading.set(false))).subscribe({
      next: (t) => this.teachers.set(t),
      error: () => this.teachersFailed.set(true),
    });
  }

  protected open(mode: Mode, s: ClassTeacherSection): void {
    this.mode.set(mode);
    this.target.set(s);
    this.pick.set('');
    this.reason.set('');
    this.confirmMultiple.set(false);
    if (mode === 'assign') this.loadTeachers();
    if (mode === 'history') {
      this.history.set([]);
      this.historyLoading.set(true);
      this.api.history(this.yearId(), s.sectionId).pipe(finalize(() => this.historyLoading.set(false))).subscribe({ next: (h) => this.history.set(h), error: () => undefined });
    }
  }

  protected close(): void {
    this.mode.set(null);
    this.target.set(null);
  }

  protected save(): void {
    const t = this.target();
    if (!t || !this.canSave()) return;
    this.busy.set(true);
    const reason = this.reason().trim() === '' ? null : this.reason().trim();
    const call = this.mode() === 'remove'
      ? this.api.unassign(this.yearId(), t.sectionId, reason)
      : this.api.assign({ academicYearId: this.yearId(), sectionId: t.sectionId, employeeId: this.pick(), reason, confirmMultipleSections: this.confirmMultiple() });
    call.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.toast.success(this.mode() === 'remove' ? `Class teacher of ${t.className} - ${t.sectionName} removed.` : `Class teacher of ${t.className} - ${t.sectionName} saved.`);
        this.close();
        this.load(this.yearId());
      },
      error: () => undefined,   // the server's reason (closed year, not eligible, ...) is already toasted
    });
  }
}
