import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import {
  ButtonComponent,
  CardComponent,
  IconComponent,
  InputComponent,
  PageHeaderComponent,
  PaginationComponent,
  SortChange,
  SortDirection,
  TableColumn,
  TableComponent,
} from '../../../../shared/components';
import { SectionFormModalComponent } from '../../components/section-form-modal/section-form-modal.component';
import { Section, SectionRow } from '../../models/section.model';
import { SectionService } from '../../services/section.service';

const COLUMNS: TableColumn<SectionRow>[] = [
  { key: 'className', label: 'Class', sortable: true },
  { key: 'sectionName', label: 'Section Name', sortable: true },
  { key: 'maxStrength', label: 'Max Strength', sortable: true },
  { key: 'statusName', label: 'Status', type: 'status', sortable: true },
];

const byText = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'base', numeric: true });

/** The signed-in school's own sections (the server scopes everything to the token's school). */
@Component({
  selector: 'app-section-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, SectionFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './section-list.component.scss',
  templateUrl: './section-list.component.html',
})
export class SectionListComponent implements OnInit {
  private readonly service = inject(SectionService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Section' }];

  protected readonly sections = signal<Section[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { section: null } = create; { section } = edit */
  protected readonly modal = signal<{ section: Section | null } | null>(null);

  protected readonly search = signal('');
  // The Class column sorts in the classes' own display order (Nursery before Class 1), then by section name
  protected readonly sortKey = signal<string | null>('className');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.sections().map((s) => this.toRow(s)).filter(
      (r) => !q || [r.className, r.sectionName, r.maxStrength, r.statusName].some((t) => t.toLowerCase().includes(q)),
    );
    if (!key) return rows;

    return [...rows].sort((a, b) => {
      let primary: number;
      if (key === 'className') primary = a.classSequenceOrder - b.classSequenceOrder || byText(a.className, b.className);
      else if (key === 'maxStrength') primary = (Number(a.maxStrength) || 0) - (Number(b.maxStrength) || 0); // blanks (0) first when ascending
      else primary = byText(String(a[key] ?? ''), String(b[key] ?? ''));
      return primary ? primary * dir : byText(a.sectionName, b.sectionName);
    });
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed<SectionRow[]>(() => {
    const start = (this.page() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .list()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (sections) => this.sections.set(sections),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(s: Section): SectionRow {
    return {
      sectionId: s.sectionId,
      classSequenceOrder: s.classSequenceOrder,
      className: s.className,
      sectionName: s.sectionName,
      maxStrength: s.maxStrength == null ? '' : String(s.maxStrength),
      statusName: s.statusName ?? 'Inactive',
      isActive: s.statusName === 'Active',
    };
  }

  protected onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
    this.page.set(1);
  }

  protected onSort(change: SortChange): void {
    this.sortKey.set(change.key);
    this.sortDirection.set(change.direction);
    this.page.set(1);
  }

  protected onPageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
  }

  protected openCreate(): void {
    this.modal.set({ section: null });
  }

  protected openEdit(row: SectionRow): void {
    this.modal.set({ section: this.sections().find((s) => s.sectionId === row.sectionId) ?? null });
  }

  protected onSaved(saved: Section): void {
    this.modal.set(null);
    this.sections.update((list) => (list.some((s) => s.sectionId === saved.sectionId) ? list.map((s) => (s.sectionId === saved.sectionId ? saved : s)) : [...list, saved]));
  }

  protected async toggleStatus(row: SectionRow): Promise<void> {
    const deactivating = row.isActive;
    const label = `${row.className} – ${row.sectionName}`;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} section?`,
      message: deactivating
        ? `"${label}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${label}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.sectionId);
    this.service
      .toggleStatus(row.sectionId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.sections.update((list) => list.map((s) => (s.sectionId === updated.sectionId ? updated : s)));
          this.toast.success(`${label} is now ${updated.statusName}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
