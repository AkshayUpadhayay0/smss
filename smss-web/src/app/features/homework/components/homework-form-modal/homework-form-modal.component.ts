import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Observable, finalize, switchMap, of, tap } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent } from '../../../../shared/components';
import { Section } from '../../../school-setup/models/section.model';
import { SectionService } from '../../../school-setup/services/section.service';
import { TimetableLookups } from '../../../timetable/models/timetable.model';
import { TimetableService } from '../../../timetable/services/timetable.service';
import { ATTACHMENT_ACCEPT, Homework, MAX_ATTACHMENT_BYTES, TeachingCombo } from '../../models/homework.model';
import { HomeworkService } from '../../services/homework.service';
import { todayYmd } from '../../utils/homework-format';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);
const dueNotBeforeAssigned = (g: AbstractControl): ValidationErrors | null => {
  const a = g.get('assignedDate')?.value as string;
  const d = g.get('dueDate')?.value as string;
  return a && d && d < a ? { dueBeforeAssigned: true } : null;
};

/**
 * Add (homework = null) or edit one homework. A Teacher picks from the section + subject pairs their OWN timetable gives them (`combos`); a School Admin picks
 * any active section, then one of its class's subjects and the owning teacher. After creating, the optional attachment is uploaded as a second request
 * (the file's private path needs the new homework's id). The section, subject and year of an existing homework are fixed.
 */
@Component({
  selector: 'app-homework-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './homework-form-modal.component.scss',
  template: `
    <app-modal [title]="editing() ? 'Edit Homework' : 'Add Homework'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (editing()) {
          <p class="fixed">{{ homework()!.className }} – {{ homework()!.sectionName }} · {{ homework()!.subjectName }} · {{ homework()!.academicYearName }}</p>
        } @else {
          <p class="fixed">Academic year: <strong>{{ academicYearName() }}</strong></p>
          <app-select label="Section" [required]="true" placeholder="Select section…" [options]="sectionOptions()" formControlName="sectionId" [errorText]="error('sectionId')" />
          <app-select label="Subject" [required]="true" placeholder="Select subject…" [options]="subjectOptions()" formControlName="subjectId" [errorText]="error('subjectId')" />
          @if (isAdmin()) {
            <app-select label="Teacher (owner)" [required]="true" placeholder="Select teacher…" [options]="teacherOptions()" formControlName="employeeId" [errorText]="error('employeeId')" />
          }
          @if (!isAdmin() && sectionOptions().length === 0) {
            <p class="hint">Your timetable has no classes for this academic year, so there is nothing to set homework for. A School Admin adds them under Timetable.</p>
          }
        }
        <app-input label="Title" [required]="true" placeholder="e.g. Chapter 3 exercises" formControlName="title" [errorText]="error('title')" />
        <app-textarea label="Description" placeholder="What should students do?" [rows]="4" formControlName="description" [errorText]="error('description')" />
        <div class="dates">
          <app-input label="Assigned" type="date" [required]="true" formControlName="assignedDate" [errorText]="error('assignedDate')" />
          <app-input label="Due" type="date" [required]="true" formControlName="dueDate" [errorText]="error('dueDate')" />
        </div>

        <div class="attachment">
          <span class="label">Attachment <span class="optional">(optional · PDF, PNG, JPG or WEBP · up to 5 MB)</span></span>
          @if (editing() && homework()!.hasAttachment && !removed()) {
            <p class="current">
              Current file: {{ homework()!.attachmentFileName }}
              <app-button variant="danger-ghost" size="sm" [disabled]="saving()" (click)="removeAttachment()">Remove</app-button>
            </p>
          }
          @for (v of [fileVersion()]; track v) {
            <input type="file" [accept]="accept" [disabled]="saving()" (change)="onFile($event)" aria-label="Attachment" />
          }
          @if (fileError()) {
            <span class="error" role="alert">{{ fileError() }}</span>
          }
        </div>
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving() || (!editing() && sectionOptions().length === 0)" (click)="submit()">{{ saving() ? 'Saving…' : editing() ? 'Update' : 'Publish' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class HomeworkFormModalComponent implements OnInit {
  private readonly service = inject(HomeworkService);
  private readonly sections = inject(SectionService);
  private readonly timetable = inject(TimetableService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  /** The homework being edited, or null to create. */
  readonly homework = input<Homework | null>(null);
  readonly isAdmin = input(false);
  readonly academicYearId = input<number>(0);
  readonly academicYearName = input('');
  /** A Teacher's own section + subject pairs (from their timetable). Unused for a School Admin. */
  readonly combos = input<TeachingCombo[]>([]);

  /** Emitted after a successful save (the page reloads its list). */
  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly accept = ATTACHMENT_ACCEPT;
  protected readonly editing = computed(() => this.homework() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly removed = signal(false);
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal('');
  /** Bumped to re-create the file input after a failed pick. */
  protected readonly fileVersion = signal(0);

  protected readonly form = new FormGroup(
    {
      sectionId: new FormControl('', { nonNullable: true }),
      subjectId: new FormControl('', { nonNullable: true }),
      employeeId: new FormControl('', { nonNullable: true }),
      title: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(200)] }),
      description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] }),
      assignedDate: new FormControl(todayYmd(), { nonNullable: true, validators: [Validators.required] }),
      dueDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: [dueNotBeforeAssigned] },
  );
  private readonly chosenSection = toSignal(this.form.controls.sectionId.valueChanges, { initialValue: '' });

  // ---- the School Admin's choices
  private readonly allSections = signal<Section[]>([]);
  private readonly lookups = signal<TimetableLookups | null>(null);

  protected readonly sectionOptions = computed<SelectOption[]>(() => {
    if (this.isAdmin()) {
      return this.allSections()
        .filter((s) => s.statusName === 'Active')
        .sort((a, b) => a.classSequenceOrder - b.classSequenceOrder || a.sectionName.localeCompare(b.sectionName))
        .map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) }));
    }
    const seen = new Map<number, string>();
    for (const c of this.combos()) seen.set(c.sectionId, c.sectionLabel);
    return [...seen].map(([id, label]) => ({ label, value: String(id) }));
  });

  protected readonly subjectOptions = computed<SelectOption[]>(() => {
    const section = Number(this.chosenSection());
    if (!section) return [];
    if (this.isAdmin()) return (this.lookups()?.subjects ?? []).map((s) => ({ label: s.subjectName, value: String(s.subjectId) }));
    return this.combos().filter((c) => c.sectionId === section).map((c) => ({ label: c.subjectName, value: String(c.subjectId) }));
  });

  protected readonly teacherOptions = computed<SelectOption[]>(() => (this.lookups()?.teachers ?? []).map((t) => ({ label: `${t.fullName} (${t.employeeId})`, value: t.employeeId })));

  ngOnInit(): void {
    const h = this.homework();
    if (h) this.form.patchValue({ title: h.title, description: h.description ?? '', assignedDate: h.assignedDate, dueDate: h.dueDate });
    else if (this.isAdmin()) {
      this.form.controls.employeeId.addValidators(Validators.required);
      this.sections.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.allSections.set(l), error: () => undefined });
    }
    if (!h) {
      this.form.controls.sectionId.addValidators(Validators.required);
      this.form.controls.subjectId.addValidators(Validators.required);
      this.form.controls.sectionId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((section) => {
        this.form.controls.subjectId.setValue('');
        this.lookups.set(null);
        if (this.isAdmin() && section) this.timetable.lookups(Number(section)).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
      });
      // a single choice needs no click
      if (!this.isAdmin() && this.sectionOptions().length === 1) this.form.controls.sectionId.setValue(this.sectionOptions()[0].value);
    }
    // the due date follows the assigned date while it is blank or would fall before it
    this.form.controls.assignedDate.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((assigned) => {
      const due = this.form.controls.dueDate.value;
      if (assigned && due && due < assigned) this.form.controls.dueDate.setValue(assigned);
    });
  }

  protected error(name: 'sectionId' | 'subjectId' | 'employeeId' | 'title' | 'description' | 'assignedDate' | 'dueDate'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (name === 'dueDate' && this.form.hasError('dueBeforeAssigned')) return 'The due date can not be before the assigned date';
    return undefined;
  }

  protected onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const picked = input.files?.[0] ?? null;
    this.fileError.set('');
    this.file.set(null);
    if (!picked) return;
    const ext = picked.name.slice(picked.name.lastIndexOf('.')).toLowerCase();
    if (!ATTACHMENT_ACCEPT.split(',').includes(ext)) this.fileError.set('Only PDF, PNG, JPG or WEBP files are allowed.');
    else if (picked.size === 0) this.fileError.set('The file is empty.');
    else if (picked.size > MAX_ATTACHMENT_BYTES) this.fileError.set('A file must be 5 MB or smaller.');
    else {
      this.file.set(picked);
      return;
    }
    this.fileVersion.update((v) => v + 1); // clear the rejected pick
  }

  protected async removeAttachment(): Promise<void> {
    const h = this.homework();
    if (!h) return;
    const ok = await this.confirmDialog.confirm({ title: 'Remove attachment?', message: `"${h.attachmentFileName}" will be deleted from this homework.`, confirmText: 'Remove', variant: 'danger' });
    if (!ok) return;
    this.saving.set(true);
    this.service
      .removeAttachment(h.homeworkId)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.removed.set(true);
          this.toast.success('Attachment removed.');
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid || this.fileError()) return;

    const v = this.form.getRawValue();
    const text = { title: v.title.trim(), description: v.description.trim() === '' ? null : v.description.trim() };
    const editing = this.homework();
    const save$: Observable<Homework> = editing
      ? this.service.update(editing.homeworkId, { ...text, assignedDate: v.assignedDate, dueDate: v.dueDate })
      : this.service.create({
          academicYearId: this.academicYearId() || undefined,
          sectionId: Number(v.sectionId),
          subjectId: Number(v.subjectId),
          employeeId: this.isAdmin() ? v.employeeId : undefined,
          assignedDate: v.assignedDate,
          dueDate: v.dueDate,
          ...text,
        });

    this.saving.set(true);
    const file = this.file();
    save$
      .pipe(
        switchMap((saved) => {
          if (!file) return of({ saved, attachmentFailed: false });
          // the homework exists now: if only the file fails, say so and keep the homework
          return new Observable<{ saved: Homework; attachmentFailed: boolean }>((subscriber) => {
            this.service.uploadAttachment(saved.homeworkId, file).subscribe({
              next: (withFile) => { subscriber.next({ saved: withFile, attachmentFailed: false }); subscriber.complete(); },
              error: () => { subscriber.next({ saved, attachmentFailed: true }); subscriber.complete(); },
            });
          });
        }),
        tap(({ attachmentFailed }) => (attachmentFailed ? this.toast.warning('The homework was saved, but its attachment could not be uploaded. Open it again to retry.') : undefined)),
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ attachmentFailed }) => {
          if (!attachmentFailed) this.toast.success(`Homework ${editing ? 'updated' : 'published'} successfully.`);
          this.saved.emit();
        },
        // Already toasted by the interceptor (e.g. the 403 naming a subject the teacher does not teach to that section); the modal stays open.
        error: () => undefined,
      });
  }
}
