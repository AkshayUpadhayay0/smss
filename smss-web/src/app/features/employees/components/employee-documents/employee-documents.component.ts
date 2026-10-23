import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { formatDate } from '../../../admissions/models/admission.model';
import { EmployeeDocument, EmployeeDocumentType, formatFileSize } from '../../models/employee.model';
import { EmployeeService } from '../../services/employee.service';

export const DOCUMENT_ACCEPT = '.pdf,.png,.jpg,.jpeg,.webp';
export const DOCUMENT_MAX_BYTES = 5 * 1024 * 1024;
const ALLOWED = ['.pdf', '.png', '.jpg', '.jpeg', '.webp'];

/** Upload, download and remove an employee's documents. Files are private: they are fetched through the authenticated API, never by URL. */
@Component({
  selector: 'app-employee-documents',
  imports: [ReactiveFormsModule, RouterLink, ButtonComponent, CardComponent, IconComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './employee-documents.component.scss',
  templateUrl: './employee-documents.component.html',
})
export class EmployeeDocumentsComponent implements OnInit {
  private readonly service = inject(EmployeeService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  readonly employeeId = input.required<string>();
  /** The school's document types that apply to employees (from the lookups). */
  readonly documentTypes = input<EmployeeDocumentType[]>([]);

  protected readonly accept = DOCUMENT_ACCEPT;
  protected readonly fmtDate = formatDate;
  protected readonly size = formatFileSize;

  protected readonly documents = signal<EmployeeDocument[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly uploading = signal(false);
  protected readonly busyId = signal<number | null>(null);

  protected readonly typeControl = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly remarksControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | undefined>(undefined);
  protected readonly typeError = signal<string | undefined>(undefined);
  protected readonly fileVersion = signal(0);

  protected readonly typeOptions = computed<SelectOption[]>(() => this.documentTypes().map((t) => ({ label: t.isRequired ? `${t.name} (required)` : t.name, value: String(t.id) })));

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .documents(this.employeeId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => this.documents.set(d),
        error: () => this.loadFailed.set(true), // already toasted by the interceptor
      });
  }

  protected onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const f = input.files?.[0] ?? null;
    this.fileError.set(undefined);
    this.file.set(null);
    if (!f) return;
    const ext = f.name.slice(f.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED.includes(ext)) this.fileError.set('Only PDF, PNG, JPG or WEBP files are allowed.');
    else if (f.size === 0) this.fileError.set('The file is empty.');
    else if (f.size > DOCUMENT_MAX_BYTES) this.fileError.set('A document must be 5 MB or smaller.');
    else this.file.set(f);
    if (this.fileError()) input.value = '';
  }

  protected upload(): void {
    if (this.uploading()) return;
    this.typeError.set(this.typeControl.invalid ? 'Select a document type' : undefined);
    if (!this.file() && !this.fileError()) this.fileError.set('Choose a file');
    if (this.typeControl.invalid || !this.file() || this.remarksControl.invalid) return;

    this.uploading.set(true);
    this.service
      .uploadDocument(this.employeeId(), this.file()!, Number(this.typeControl.value), this.remarksControl.value.trim() || null)
      .pipe(finalize(() => this.uploading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (doc) => {
          this.documents.update((l) => [doc, ...l]);
          this.toast.success('Document uploaded.');
          this.typeControl.reset('');
          this.remarksControl.reset('');
          this.file.set(null);
          this.fileVersion.update((v) => v + 1); // a fresh file input
        },
        error: () => undefined, // the server's reason is toasted by the interceptor; the form keeps its values
      });
  }

  protected download(doc: EmployeeDocument): void {
    if (this.busyId() !== null) return;
    this.busyId.set(doc.employeeDocumentId);
    this.service
      .downloadDocument(this.employeeId(), doc.employeeDocumentId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = doc.fileName;
          a.click();
          URL.revokeObjectURL(url);
        },
        error: () => undefined, // already toasted by the interceptor
      });
  }

  protected async remove(doc: EmployeeDocument): Promise<void> {
    const ok = await this.confirmDialog.confirm({
      title: 'Remove document?',
      message: `"${doc.fileName}" (${doc.documentTypeName}) will be hidden from this employee. The record is kept for the audit trail.`,
      confirmText: 'Remove',
      variant: 'danger',
    });
    if (!ok) return;
    this.busyId.set(doc.employeeDocumentId);
    this.service
      .removeDocument(this.employeeId(), doc.employeeDocumentId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.documents.update((l) => l.filter((d) => d.employeeDocumentId !== doc.employeeDocumentId));
          this.toast.success('Document removed.');
        },
        error: () => undefined,
      });
  }
}
