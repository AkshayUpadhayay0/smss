import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { ROLES } from '../../../../core/config/route-roles';
import { ApiService } from '../../../../core/services/api.service';
import { AuthService } from '../../../../core/services/auth.service';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, SelectOption, TableColumn, TableComponent } from '../../../../shared/components';
import { AcademicYear } from '../../../school-setup/models/academic-year.model';
import { Section } from '../../../school-setup/models/section.model';
import { AcademicYearService } from '../../../school-setup/services/academic-year.service';
import { SectionService } from '../../../school-setup/services/section.service';
import { TimetableService } from '../../../timetable/services/timetable.service';
import { HomeworkFormModalComponent } from '../../components/homework-form-modal/homework-form-modal.component';
import { Homework, HomeworkRow, TeachingCombo } from '../../models/homework.model';
import { HomeworkService } from '../../services/homework.service';
import { combosFromGrid, day, saveBlob, sectionLabel } from '../../utils/homework-format';

interface SubjectItem { subjectId: number; subjectName: string; statusName?: string | null }

const COLUMNS: TableColumn<HomeworkRow>[] = [
  { key: 'title', label: 'Title' },
  { key: 'group', label: 'Class' },
  { key: 'subjectName', label: 'Subject' },
  { key: 'teacherName', label: 'Teacher' },
  { key: 'assigned', label: 'Assigned' },
  { key: 'due', label: 'Due' },
  { key: 'attachment', label: 'File', type: 'template' },
];

/**
 * Teacher: the homework they set. School Admin: the whole school. Filter by academic year, section and subject; add, edit, delete, and download the
 * attachment. A teacher picks sections and subjects from their OWN timetable (Timetable/my), so they can only add homework for what they teach.
 */
@Component({
  selector: 'app-homework-list',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, PageHeaderComponent, SelectComponent, TableComponent, HomeworkFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './homework-list.component.scss',
  template: `
    <app-page-header title="Homework" [subtitle]="isAdmin() ? 'Homework set for every class' : 'Homework you have set'" icon="graduation-cap" [breadcrumbs]="breadcrumbs">
      <app-button [disabled]="!yearControl.value" (click)="modal.set({ homework: null })"><app-icon name="plus" [size]="16" />Add Homework</app-button>
    </app-page-header>

    <app-card>
      <div class="filters">
        <app-select label="Academic year" placeholder="Select year…" [options]="yearOptions()" [formControl]="yearControl" />
        <app-select label="Section" [options]="sectionOptions()" [formControl]="sectionControl" />
        <app-select label="Subject" [options]="subjectOptions()" [formControl]="subjectControl" />
        <p class="hint">{{ rows().length }} {{ rows().length === 1 ? 'homework' : 'homework items' }}</p>
      </div>
      @if (!isAdmin() && !combosLoading() && combos().length === 0 && yearControl.value) {
        <p class="note">Your timetable has no classes in this academic year, so you can not set homework yet.</p>
      }
    </app-card>

    <app-card [noPadding]="true">
      <app-table [columns]="columns" [rows]="rows()" [loading]="loading()" [hasActions]="true" [emptyMessage]="loadFailed() ? 'Could not load homework.' : 'No homework matches.'">
        <ng-template #cell let-row>
          @if (row.hasAttachment) {
            <app-button variant="secondary" size="sm" [disabled]="busyId() === row.homeworkId" aria-label="Download attachment" (click)="download(row)">Download</app-button>
          } @else {
            <span class="muted">—</span>
          }
        </ng-template>
        <ng-template #rowActions let-row>
          <app-button variant="secondary" size="sm" aria-label="Edit" title="Edit" (click)="openEdit(row)"><app-icon name="pencil" [size]="14" /></app-button>
          <app-button variant="danger-ghost" size="sm" aria-label="Delete" title="Delete" [disabled]="busyId() === row.homeworkId" (click)="remove(row)"><app-icon name="trash-2" [size]="14" /></app-button>
        </ng-template>
      </app-table>
    </app-card>

    @if (modal(); as m) {
      <app-homework-form-modal
        [homework]="m.homework"
        [isAdmin]="isAdmin()"
        [academicYearId]="yearId()"
        [academicYearName]="yearName()"
        [combos]="combos()"
        (saved)="onSaved()"
        (closed)="modal.set(null)"
      />
    }
  `,
})
export class HomeworkListComponent implements OnInit {
  private readonly service = inject(HomeworkService);
  private readonly auth = inject(AuthService);
  private readonly years = inject(AcademicYearService);
  private readonly sections = inject(SectionService);
  private readonly timetable = inject(TimetableService);
  private readonly api = inject(ApiService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'Homework' }];
  protected readonly isAdmin = computed(() => this.auth.currentUserRoles().includes(ROLES.SCHOOL_ADMIN));

  protected readonly yearControl = new FormControl('', { nonNullable: true });
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly subjectControl = new FormControl('', { nonNullable: true });
  protected readonly yearId = signal(0);
  private readonly sectionId = signal('');
  private readonly allYears = signal<AcademicYear[]>([]);
  private readonly allSections = signal<Section[]>([]);
  private readonly allSubjects = signal<SubjectItem[]>([]);
  /** A teacher's own section + subject pairs for the chosen year (from the timetable). */
  protected readonly combos = signal<TeachingCombo[]>([]);
  protected readonly combosLoading = signal(false);

  protected readonly yearOptions = computed<SelectOption[]>(() =>
    this.allYears().filter((y) => y.statusName === 'Active').map((y) => ({ label: y.isCurrent ? `${y.yearName} (current)` : y.yearName, value: String(y.academicYearId) })),
  );
  protected readonly yearName = computed(() => this.allYears().find((y) => y.academicYearId === this.yearId())?.yearName ?? '');
  protected readonly sectionOptions = computed<SelectOption[]>(() => {
    const options: SelectOption[] = this.isAdmin()
      ? this.allSections().filter((s) => s.statusName === 'Active').sort((a, b) => a.classSequenceOrder - b.classSequenceOrder || a.sectionName.localeCompare(b.sectionName)).map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) }))
      : [...new Map(this.combos().map((c) => [c.sectionId, c.sectionLabel])).entries()].map(([id, label]) => ({ label, value: String(id) }));
    return [{ label: 'All sections', value: '' }, ...options];
  });
  protected readonly subjectOptions = computed<SelectOption[]>(() => {
    const options: SelectOption[] = this.isAdmin()
      ? this.allSubjects().filter((s) => s.statusName !== 'Inactive').map((s) => ({ label: s.subjectName, value: String(s.subjectId) }))
      : [...new Map(this.combos().filter((c) => !this.sectionId() || String(c.sectionId) === this.sectionId()).map((c) => [c.subjectId, c.subjectName])).entries()].map(([id, label]) => ({ label, value: String(id) }));
    return [{ label: 'All subjects', value: '' }, ...options];
  });

  protected readonly items = signal<Homework[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { homework: null } = create; { homework } = edit */
  protected readonly modal = signal<{ homework: Homework | null } | null>(null);

  protected readonly rows = computed<HomeworkRow[]>(() =>
    this.items().map((h) => ({
      homeworkId: h.homeworkId, title: h.title, group: sectionLabel(h), subjectName: h.subjectName, teacherName: h.teacherName,
      assigned: day(h.assignedDate), due: day(h.dueDate), hasAttachment: h.hasAttachment,
    })),
  );

  ngOnInit(): void {
    this.yearControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.yearId.set(Number(v) || 0);
      this.sectionControl.setValue('', { emitEvent: false });
      this.subjectControl.setValue('', { emitEvent: false });
      this.sectionId.set('');
      this.loadCombos();
      this.load();
    });
    this.sectionControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((v) => {
      this.sectionId.set(v);
      if (!this.isAdmin()) this.subjectControl.setValue('', { emitEvent: false }); // the subjects on offer depend on the section
      this.load();
    });
    this.subjectControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());

    this.years.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (years) => {
        this.allYears.set(years);
        const active = years.filter((y) => y.statusName === 'Active');
        const year = active.find((y) => y.isCurrent) ?? active[0];
        if (year) this.yearControl.setValue(String(year.academicYearId)); // emits: loads the list
      },
      error: () => undefined, // already toasted by the interceptor
    });
    if (this.isAdmin()) {
      this.sections.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.allSections.set(l), error: () => undefined });
      this.api.get<SubjectItem[]>('/api/Subjects').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (l) => this.allSubjects.set(l ?? []), error: () => undefined });
    }
  }

  /** The teacher's own timetable for the chosen year says which sections and subjects they can set homework for. */
  private loadCombos(): void {
    if (this.isAdmin() || !this.yearId()) return;
    this.combosLoading.set(true);
    this.timetable
      .myGrid(this.yearId())
      .pipe(finalize(() => this.combosLoading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (grid) => this.combos.set(combosFromGrid(grid)), error: () => this.combos.set([]) });
  }

  protected load(): void {
    if (!this.yearId()) return;
    const filters = {
      academicYearId: this.yearId(),
      sectionId: Number(this.sectionControl.value) || undefined,
      subjectId: Number(this.subjectControl.value) || undefined,
    };
    this.loading.set(true);
    this.loadFailed.set(false);
    (this.isAdmin() ? this.service.list(filters) : this.service.my(filters))
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (list) => this.items.set(list), error: () => this.loadFailed.set(true) });
  }

  protected openEdit(row: HomeworkRow): void {
    this.modal.set({ homework: this.items().find((h) => h.homeworkId === row.homeworkId) ?? null });
  }

  protected onSaved(): void {
    this.modal.set(null);
    this.load();
  }

  protected download(row: HomeworkRow): void {
    const item = this.items().find((h) => h.homeworkId === row.homeworkId);
    if (!item || this.busyId() !== null) return;
    this.busyId.set(item.homeworkId);
    this.service
      .downloadAttachment(item.homeworkId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (blob) => saveBlob(blob, item.attachmentFileName ?? `homework-${item.homeworkId}`), error: () => undefined });
  }

  protected async remove(row: HomeworkRow): Promise<void> {
    const confirmed = await this.confirmDialog.confirm({
      title: 'Delete homework?',
      message: `"${row.title}" (${row.group}, ${row.subjectName}) will be hidden from students and teachers.`,
      confirmText: 'Delete',
      variant: 'danger',
    });
    if (!confirmed) return;
    this.busyId.set(row.homeworkId);
    this.service
      .remove(row.homeworkId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: () => {
          this.items.update((l) => l.filter((h) => h.homeworkId !== row.homeworkId));
          this.toast.success('Homework deleted.');
        },
        error: () => this.load(), // e.g. someone else already removed it: show the truth
      });
  }
}
