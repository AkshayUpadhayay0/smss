import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Observable, finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, InputComponent, ModalComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { TeachingCombo } from '../../../homework/models/homework.model';
import { todayYmd } from '../../../homework/utils/homework-format';
import { Section } from '../../../school-setup/models/section.model';
import { SectionService } from '../../../school-setup/services/section.service';
import { TimetableLookups } from '../../../timetable/models/timetable.model';
import { TimetableService } from '../../../timetable/services/timetable.service';
import { AssessmentDetail, AssessmentMarkInput, AssessmentType, MAX_MARKS_LIMIT, RosterStudent } from '../../models/assessment.model';
import { AssessmentService } from '../../services/assessment.service';

const notBlank = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '').trim() === '' ? { blank: true } : null);
const notFuture = (c: AbstractControl): ValidationErrors | null => (String(c.value ?? '') > todayYmd() ? { future: true } : null);
const twoDecimals = (v: number) => Math.round(v * 100) / 100 === v;

/**
 * Record (assessment = null) or edit one test. The roster of the section is listed with a marks box and an Absent toggle per student (Absent disables and
 * clears the marks box). A Teacher picks from the section + subject pairs of their OWN timetable (`combos`); a School Admin picks any active section, one of
 * its class's subjects and the owning teacher. The server insists the marks cover every active student, so the form lists them all and asks for each.
 * The section, subject and year of an existing test are fixed.
 */
@Component({
  selector: 'app-assessment-form-modal',
  imports: [ReactiveFormsModule, ModalComponent, ButtonComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './assessment-form-modal.component.scss',
  template: `
    <app-modal [title]="editing() ? 'Edit Test' : 'Record Test'" [dismissible]="!saving()" (closed)="closed.emit()">
      <form class="form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        @if (editing()) {
          <p class="fixed">{{ assessment()!.className }} – {{ assessment()!.sectionName }} · {{ assessment()!.subjectName }} · {{ assessment()!.academicYearName }}</p>
        } @else {
          <p class="fixed">Academic year: <strong>{{ academicYearName() }}</strong></p>
          <div class="grid">
            <app-select label="Section" [required]="true" placeholder="Select section…" [options]="sectionOptions()" formControlName="sectionId" [errorText]="error('sectionId')" />
            <app-select label="Subject" [required]="true" placeholder="Select subject…" [options]="subjectOptions()" formControlName="subjectId" [errorText]="error('subjectId')" />
            @if (isAdmin()) {
              <app-select label="Teacher (owner)" [required]="true" placeholder="Select teacher…" [options]="teacherOptions()" formControlName="employeeId" [errorText]="error('employeeId')" />
            }
          </div>
          @if (!isAdmin() && sectionOptions().length === 0) {
            <p class="hint">Your timetable has no classes for this academic year, so there is nothing to record a test for. A School Admin adds them under Timetable.</p>
          }
        }

        <div class="grid">
          <app-input label="Title" [required]="true" placeholder="e.g. Fractions quiz" formControlName="title" [errorText]="error('title')" />
          @if (!editing()) {
            <app-select label="Type" [required]="true" placeholder="Select type…" [options]="typeOptions()" formControlName="assessmentTypeId" [errorText]="error('assessmentTypeId')" />
          }
          <app-input label="Test date" type="date" [required]="true" formControlName="testDate" [errorText]="error('testDate')" />
          <app-input label="Maximum marks" type="number" [required]="true" placeholder="e.g. 20" formControlName="maxMarks" [errorText]="error('maxMarks')" />
        </div>

        <section class="roster" aria-label="Marks">
          <h3>Marks <span class="count">{{ rows.length }} {{ rows.length === 1 ? 'student' : 'students' }}</span></h3>
          @if (rosterLoading()) {
            <p class="hint" role="status">Loading the class list…</p>
          } @else if (rows.length === 0) {
            <p class="hint">{{ editing() ? 'This section has no active students.' : 'Choose a section and a subject to list its students.' }}</p>
          } @else {
            <div class="head"><span>Roll</span><span>Student</span><span>Marks{{ maxText() }}</span><span>Absent</span></div>
            <ul formArrayName="rows">
              @for (row of rows.controls; track row.controls.studentId.value; let i = $index) {
                <li [formGroupName]="i" [class.invalid]="rowInvalid(i)">
                  <span class="roll">{{ meta()[row.controls.studentId.value]?.roll || '—' }}</span>
                  <span class="name">{{ meta()[row.controls.studentId.value]?.name }}</span>
                  <input class="marks" type="number" step="0.01" min="0" inputmode="decimal" formControlName="marks" [attr.aria-label]="'Marks for ' + meta()[row.controls.studentId.value]?.name" [placeholder]="row.controls.absent.value ? 'Absent' : ''" />
                  <label class="absent"><input type="checkbox" formControlName="absent" [attr.aria-label]="'Absent: ' + meta()[row.controls.studentId.value]?.name" /></label>
                </li>
              }
            </ul>
            @if (submitted() && rowErrors() > 0) {
              <p class="error" role="alert">{{ rowErrors() }} {{ rowErrors() === 1 ? 'student needs' : 'students need' }} marks (between 0 and the maximum, two decimals at most) or an Absent tick.</p>
            }
          }
          @if (leftStudents().length > 0) {
            <p class="hint">Not changed: {{ leftStudents().length }} {{ leftStudents().length === 1 ? 'student has' : 'students have' }} left the section and keep{{ leftStudents().length === 1 ? 's' : '' }} the result already recorded ({{ leftNames() }}).</p>
          }
        </section>
        <button type="submit" hidden tabindex="-1"></button>
      </form>

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving() || rows.length === 0" (click)="submit()">{{ saving() ? 'Saving…' : editing() ? 'Update' : 'Save test' }}</app-button>
      </ng-container>
    </app-modal>
  `,
})
export class AssessmentFormModalComponent implements OnInit {
  private readonly service = inject(AssessmentService);
  private readonly sections = inject(SectionService);
  private readonly timetable = inject(TimetableService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  /** The test being edited (with its marks), or null to record a new one. */
  readonly assessment = input<AssessmentDetail | null>(null);
  readonly isAdmin = input(false);
  readonly academicYearId = input<number>(0);
  readonly academicYearName = input('');
  /** A Teacher's own section + subject pairs (from their timetable). Unused for a School Admin. */
  readonly combos = input<TeachingCombo[]>([]);
  readonly types = input<AssessmentType[]>([]);

  readonly saved = output<void>();
  readonly closed = output<void>();

  protected readonly editing = computed(() => this.assessment() !== null);
  protected readonly saving = signal(false);
  protected readonly submitted = signal(false);
  protected readonly rosterLoading = signal(false);
  /** Roll number and name by student id (the form array only holds ids, marks and the absent flag). */
  protected readonly meta = signal<Record<string, { roll: string; name: string }>>({});
  protected readonly leftStudents = signal<{ studentId: string; fullName: string }[]>([]);
  protected readonly leftNames = computed(() => this.leftStudents().map((s) => s.fullName).join(', '));

  protected readonly form = new FormGroup({
    sectionId: new FormControl('', { nonNullable: true }),
    subjectId: new FormControl('', { nonNullable: true }),
    employeeId: new FormControl('', { nonNullable: true }),
    assessmentTypeId: new FormControl('', { nonNullable: true }),
    title: new FormControl('', { nonNullable: true, validators: [Validators.required, notBlank, Validators.maxLength(200)] }),
    testDate: new FormControl(todayYmd(), { nonNullable: true, validators: [Validators.required, notFuture] }),
    maxMarks: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.min(0.01), Validators.max(MAX_MARKS_LIMIT)] }),
    rows: new FormArray<FormGroup<{ studentId: FormControl<string>; marks: FormControl<string>; absent: FormControl<boolean> }>>([]),
  });
  protected get rows() {
    return this.form.controls.rows;
  }
  private readonly chosenSection = toSignal(this.form.controls.sectionId.valueChanges, { initialValue: '' });
  private readonly chosenSubject = toSignal(this.form.controls.subjectId.valueChanges, { initialValue: '' });
  private readonly maxValue = toSignal(this.form.controls.maxMarks.valueChanges, { initialValue: '' });
  protected readonly maxText = computed(() => (this.maxValue() ? ` (out of ${this.maxValue()})` : ''));

  // ---- the School Admin's choices
  private readonly allSections = signal<Section[]>([]);
  private readonly lookups = signal<TimetableLookups | null>(null);

  protected readonly typeOptions = computed<SelectOption[]>(() => this.types().map((t) => ({ label: t.typeName, value: String(t.assessmentTypeId) })));
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
    const a = this.assessment();
    if (a) {
      this.form.patchValue({ title: a.title, testDate: a.testDate, maxMarks: String(a.maxMarks) });
      this.loadRoster(a.academicYearId, a.sectionId, a.subjectId, a);
    } else {
      this.form.controls.sectionId.addValidators(Validators.required);
      this.form.controls.subjectId.addValidators(Validators.required);
      this.form.controls.assessmentTypeId.addValidators(Validators.required);
      if (this.isAdmin()) {
        this.form.controls.employeeId.addValidators(Validators.required);
        this.sections.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.allSections.set(l), error: () => undefined });
      }
      this.form.controls.sectionId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((section) => {
        this.form.controls.subjectId.setValue('');
        this.lookups.set(null);
        this.clearRows();
        if (this.isAdmin() && section) this.timetable.lookups(Number(section)).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.lookups.set(l), error: () => undefined });
      });
      // the roster of a (section, subject) pair is what the server will insist on: list it as soon as both are chosen
      this.form.controls.subjectId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((subject) => {
        this.clearRows();
        const section = Number(this.form.controls.sectionId.value);
        if (section && subject) this.loadRoster(this.academicYearId(), section, Number(subject), null);
      });
      if (this.types().length === 1) this.form.controls.assessmentTypeId.setValue(String(this.types()[0].assessmentTypeId));
      if (!this.isAdmin() && this.sectionOptions().length === 1) this.form.controls.sectionId.setValue(this.sectionOptions()[0].value);
    }
  }

  private clearRows(): void {
    this.rows.clear();
    this.meta.set({});
    this.leftStudents.set([]);
  }

  /** Lists the section's active students. When editing, each starts on their saved mark; a student who joined later starts empty; one who left is only noted. */
  private loadRoster(academicYearId: number, sectionId: number, subjectId: number, saved: AssessmentDetail | null): void {
    this.rosterLoading.set(true);
    this.service
      .roster(academicYearId, sectionId, subjectId)
      .pipe(finalize(() => this.rosterLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (roster: RosterStudent[]) => {
          const byId = new Map((saved?.marks ?? []).map((m) => [m.studentId, m]));
          this.meta.set(Object.fromEntries(roster.map((r) => [r.studentId, { roll: r.rollNumber ?? '', name: r.fullName }])));
          this.rows.clear();
          for (const r of roster) {
            const m = byId.get(r.studentId);
            this.rows.push(this.rowGroup(r.studentId, m?.isAbsent ? '' : m?.obtainedMarks != null ? String(m.obtainedMarks) : '', !!m?.isAbsent));
          }
          const ids = new Set(roster.map((r) => r.studentId));
          this.leftStudents.set((saved?.marks ?? []).filter((m) => !ids.has(m.studentId)).map((m) => ({ studentId: m.studentId, fullName: m.fullName })));
        },
        error: () => this.clearRows(), // already toasted by the interceptor (e.g. the 403 for a subject the teacher does not teach to that section)
      });
  }

  private rowGroup(studentId: string, marks: string, absent: boolean) {
    const group = new FormGroup({
      studentId: new FormControl(studentId, { nonNullable: true }),
      marks: new FormControl({ value: marks, disabled: absent }, { nonNullable: true }),
      absent: new FormControl(absent, { nonNullable: true }),
    });
    // Absent switches the marks box off (and empties it); un-ticking switches it back on
    group.controls.absent.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((on) => {
      if (on) { group.controls.marks.setValue(''); group.controls.marks.disable(); } else group.controls.marks.enable();
    });
    return group;
  }

  protected error(name: 'sectionId' | 'subjectId' | 'employeeId' | 'assessmentTypeId' | 'title' | 'testDate' | 'maxMarks'): string | undefined {
    const c = this.form.controls[name];
    if (!(c.touched || this.submitted())) return undefined;
    if (c.hasError('required') || c.hasError('blank')) return 'This field is required';
    if (c.hasError('maxlength')) return `Maximum ${c.getError('maxlength').requiredLength} characters`;
    if (c.hasError('future')) return 'The test date can not be in the future';
    if (c.hasError('min') || c.hasError('max')) return `Enter a number from 0.01 to ${MAX_MARKS_LIMIT}`;
    if (name === 'maxMarks' && c.value !== '' && !twoDecimals(Number(c.value))) return 'At most two decimal places';
    return undefined;
  }

  /** A row is wrong when it is not absent and its marks are missing, negative, above the maximum or have more than two decimals. */
  protected rowInvalid(i: number): boolean {
    if (!this.submitted()) return false;
    const row = this.rows.at(i);
    if (row.controls.absent.value) return false;
    const raw = row.controls.marks.value.trim();
    const max = Number(this.form.controls.maxMarks.value);
    const n = Number(raw);
    return raw === '' || Number.isNaN(n) || n < 0 || (max > 0 && n > max) || !twoDecimals(n);
  }
  /** How many rows still need attention (recomputed on every check, so it follows the typing). */
  protected rowErrors(): number {
    return this.rows.controls.filter((_, i) => this.rowInvalid(i)).length;
  }

  protected submit(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    const bad = this.rows.controls.some((_, i) => this.rowInvalid(i));
    if (this.form.invalid || this.rows.length === 0 || bad || !twoDecimals(Number(this.form.controls.maxMarks.value))) {
      this.form.updateValueAndValidity();   // refreshes the row-error count
      return;
    }

    const v = this.form.getRawValue();
    const marks: AssessmentMarkInput[] = v.rows.map((r) => ({ studentId: r.studentId, isAbsent: r.absent, obtainedMarks: r.absent ? null : Number(r.marks) }));
    const common = { title: v.title.trim(), testDate: v.testDate, maxMarks: Number(v.maxMarks), marks };
    const editing = this.assessment();
    const request$: Observable<unknown> = editing
      ? this.service.update(editing.assessmentId, common)
      : this.service.create({
          academicYearId: this.academicYearId(),
          sectionId: Number(v.sectionId),
          subjectId: Number(v.subjectId),
          assessmentTypeId: Number(v.assessmentTypeId),
          employeeId: this.isAdmin() ? v.employeeId : undefined,
          ...common,
        });

    this.saving.set(true);
    request$.pipe(finalize(() => this.saving.set(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.toast.success(`Test ${editing ? 'updated' : 'recorded'} successfully.`);
        this.saved.emit();
      },
      // Already toasted by the interceptor (e.g. the 403 naming a subject the teacher does not teach to that section); the modal stays open with the typed marks.
      error: () => undefined,
    });
  }
}
