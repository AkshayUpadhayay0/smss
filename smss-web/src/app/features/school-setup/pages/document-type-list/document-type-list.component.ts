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
import { DocumentTypeFormModalComponent } from '../../components/document-type-form-modal/document-type-form-modal.component';
import { APPLIES_TO_LABELS, DocumentType, DocumentTypeRow } from '../../models/document-type.model';
import { DocumentTypeService } from '../../services/document-type.service';

const COLUMNS: TableColumn<DocumentTypeRow>[] = [
  { key: 'documentName', label: 'Document Name', sortable: true },
  { key: 'documentCode', label: 'Document Code', sortable: true },
  { key: 'appliesToLabel', label: 'Applies To', sortable: true, type: 'badge' },
  { key: 'requiredLabel', label: 'Required', sortable: true, type: 'badge' },
  { key: 'statusName', label: 'Status', sortable: true, type: 'status' },
];

const byText = (a: unknown, b: unknown) => String(a ?? '').localeCompare(String(b ?? ''), undefined, { sensitivity: 'base', numeric: true });

/** The signed-in school's own document types (the school comes from the session; the server enforces it too). */
@Component({
  selector: 'app-document-type-list',
  imports: [ButtonComponent, CardComponent, IconComponent, InputComponent, PageHeaderComponent, PaginationComponent, TableComponent, DocumentTypeFormModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './document-type-list.component.scss',
  templateUrl: './document-type-list.component.html',
})
export class DocumentTypeListComponent implements OnInit {
  private readonly service = inject(DocumentTypeService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  protected readonly columns = COLUMNS;
  protected readonly breadcrumbs = [{ label: 'School Setup' }, { label: 'Document Types' }];

  protected readonly documentTypes = signal<DocumentType[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly busyId = signal<number | null>(null);
  /** null = closed; { documentType: null } = create; { documentType } = edit */
  protected readonly modal = signal<{ documentType: DocumentType | null } | null>(null);

  protected readonly search = signal('');
  protected readonly sortKey = signal<string | null>('documentName');
  protected readonly sortDirection = signal<SortDirection>('asc');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  private readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const key = this.sortKey();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    const rows = this.documentTypes().map((d) => this.toRow(d)).filter(
      (r) => !q || [r.documentName, r.documentCode, r.appliesToLabel, r.requiredLabel, r.statusName].some((t) => t.toLowerCase().includes(q)),
    );
    if (!key) return rows;
    return [...rows].sort((a, b) => byText(a[key], b[key]) * dir || byText(a.documentName, b.documentName));
  });

  protected readonly total = computed(() => this.filtered().length);
  protected readonly pageRows = computed<DocumentTypeRow[]>(() => {
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
        next: (items) => this.documentTypes.set(items ?? []),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  private toRow(d: DocumentType): DocumentTypeRow {
    return {
      documentTypeId: d.documentTypeId,
      documentName: d.documentName,
      documentCode: d.documentCode,
      appliesToLabel: APPLIES_TO_LABELS[d.appliesTo] ?? d.appliesTo,
      requiredLabel: d.isRequired ? 'Yes' : 'No',
      statusName: d.isActive ? 'Active' : 'Inactive',
      isActive: d.isActive,
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
    this.modal.set({ documentType: null });
  }

  protected openEdit(row: DocumentTypeRow): void {
    this.modal.set({ documentType: this.documentTypes().find((d) => d.documentTypeId === row.documentTypeId) ?? null });
  }

  protected onSaved(saved: DocumentType): void {
    this.modal.set(null);
    this.documentTypes.update((list) =>
      list.some((d) => d.documentTypeId === saved.documentTypeId) ? list.map((d) => (d.documentTypeId === saved.documentTypeId ? saved : d)) : [...list, saved],
    );
  }

  protected async toggleStatus(row: DocumentTypeRow): Promise<void> {
    const deactivating = row.isActive;
    const confirmed = await this.confirmDialog.confirm({
      title: `${deactivating ? 'Deactivate' : 'Activate'} document type?`,
      message: deactivating
        ? `"${row.documentName}" will be marked Inactive. Nothing is deleted and you can activate it again later.`
        : `"${row.documentName}" will be marked Active again.`,
      confirmText: deactivating ? 'Deactivate' : 'Activate',
      variant: deactivating ? 'danger' : 'primary',
    });
    if (!confirmed) return;

    this.busyId.set(row.documentTypeId);
    this.service
      .toggleStatus(row.documentTypeId)
      .pipe(finalize(() => this.busyId.set(null)))
      .subscribe({
        next: (updated) => {
          this.documentTypes.update((list) => list.map((d) => (d.documentTypeId === updated.documentTypeId ? updated : d)));
          this.toast.success(`${updated.documentName} is now ${updated.isActive ? 'Active' : 'Inactive'}.`);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }
}
