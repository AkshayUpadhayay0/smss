import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { SchoolClass } from '../../models/class.model';
import { SchoolLookupItem } from '../../models/school-lookup.model';
import { ClassService } from '../../services/class.service';
import { ClassSubjectService } from '../../services/class-subject.service';
import { SchoolLookupService } from '../../services/school-lookup.service';

interface SubjectOptionView {
  id: number;
  name: string;
  code: string;
  inactive: boolean;
}

/**
 * Which subjects are taught in which class. Pick a class, tick its subjects, Save: the full checkbox state is sent
 * and the server replaces that class's set (a mapping is simply present or absent — no soft status).
 */
@Component({
  selector: 'app-class-subject-mapping',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './class-subject-mapping.component.scss',
  templateUrl: './class-subject-mapping.component.html',
})
export class ClassSubjectMappingComponent implements OnInit {
  private readonly classService = inject(ClassService);
  private readonly lookups = inject(SchoolLookupService);
  private readonly mappings = inject(ClassSubjectService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Class-Subject Mapping' }];

  protected readonly classControl = new FormControl('', { nonNullable: true });
  protected readonly classOptions = signal<SelectOption[]>([]);
  private readonly subjects = signal<SchoolLookupItem[]>([]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly loadingMappings = signal(false);
  protected readonly saving = signal(false);

  private readonly selectedClassId = signal<number | null>(null);
  /** What is saved on the server for the selected class, and what is currently ticked. */
  private readonly saved = signal<ReadonlySet<number>>(new Set());
  protected readonly ticked = signal<ReadonlySet<number>>(new Set());

  protected readonly dirty = computed(() => {
    const a = this.saved();
    const b = this.ticked();
    return a.size !== b.size || [...a].some((id) => !b.has(id));
  });

  /** Active subjects, plus any inactive subject that is already mapped (so saving never silently drops it). */
  protected readonly visibleSubjects = computed<SubjectOptionView[]>(() =>
    this.subjects()
      .map((s) => ({
        id: s['subjectId'] as number,
        name: String(s['subjectName'] ?? ''),
        code: String(s['subjectCode'] ?? ''),
        inactive: s.statusName !== 'Active',
      }))
      .filter((s) => !s.inactive || this.saved().has(s.id))
      .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base', numeric: true })),
  );

  protected readonly hasClass = computed(() => this.selectedClassId() !== null);

  ngOnInit(): void {
    this.loadLookups();
    this.classControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => void this.onClassPicked(value));
  }

  protected loadLookups(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    forkJoin({ classes: this.classService.list(), subjects: this.lookups.list('Subjects') })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: ({ classes, subjects }) => {
          this.classOptions.set(classes.filter((c: SchoolClass) => c.statusName === 'Active').map((c) => ({ label: c.className, value: String(c.classId) })));
          this.subjects.set(subjects);
        },
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  /** Switching class: unsaved ticks are only discarded after the user confirms. */
  private async onClassPicked(value: string): Promise<void> {
    const previous = this.selectedClassId();
    if (this.dirty()) {
      const discard = await this.confirmDialog.confirm({
        title: 'Discard unsaved changes?',
        message: 'You have unsaved subject changes for the current class. Switching class will discard them.',
        confirmText: 'Discard changes',
        variant: 'danger',
      });
      if (!discard) {
        this.classControl.setValue(previous === null ? '' : String(previous), { emitEvent: false });
        return;
      }
    }
    this.loadMappings(value ? Number(value) : null);
  }

  private loadMappings(classId: number | null): void {
    this.selectedClassId.set(classId);
    this.saved.set(new Set());
    this.ticked.set(new Set());
    if (classId === null) return;

    this.loadingMappings.set(true);
    this.mappings
      .listByClass(classId)
      .pipe(finalize(() => this.loadingMappings.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (rows) => {
          if (this.selectedClassId() !== classId) return; // the user already switched again
          const ids = new Set(rows.map((r) => r.subjectId));
          this.saved.set(ids);
          this.ticked.set(new Set(ids));
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }

  protected toggle(id: number, checked: boolean): void {
    this.ticked.update((set) => {
      const next = new Set(set);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  protected reset(): void {
    this.ticked.set(new Set(this.saved()));
  }

  protected save(): void {
    const classId = this.selectedClassId();
    if (classId === null || this.saving() || !this.dirty()) return;

    this.saving.set(true);
    this.mappings
      .setSubjects(classId, [...this.ticked()])
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (rows) => {
          const ids = new Set(rows.map((r) => r.subjectId));
          this.saved.set(ids);
          this.ticked.set(new Set(ids));
          this.toast.success('Class subjects saved successfully.');
        },
        error: () => undefined, // already toasted by the interceptor; the ticks stay so nothing is lost
      });
  }
}
