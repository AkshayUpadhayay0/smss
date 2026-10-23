import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, finalize, of } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { DocumentTypeService } from '../../../school-setup/services/document-type.service';
import { formatFileSize } from '../../../employees/models/employee.model';
import { AdmissionDocument, formatDate } from '../../models/admission.model';
import { AdmissionService } from '../../services/admission.service';

export const ADMISSION_DOCUMENT_ACCEPT = '.pdf,.png,.jpg,.jpeg,.webp';
export const ADMISSION_DOCUMENT_MAX_BYTES = 5 * 1024 * 1024;
const ALLOWED = ['.pdf', '.png', '.jpg', '.jpeg', '.webp'];

/**
 * An application's documents: upload, download and remove. Files are private: they are fetched through the authenticated API, never by URL.
 * Only the school's active document types that apply to students (STUDENT or BOTH) can be chosen. When the application is Rejected, Withdrawn or
 * Admitted the list stays readable but uploading and removing are switched off (the server refuses them too).
 */
@Component({
  selector: 'app-admission-documents',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './admission-documents.component.scss',
  template: `
    <app-card title="Documents">
      @if (!locked()) {
        <div class="upload">
          <app-select label="Document type" [required]="true" placeholder="Select type…" [options]="typeOptions()" [formControl]="typeControl" [errorText]="typeError()" />
          <app-input label="Remarks" placeholder="Optional" [formControl]="remarksControl" />
          <div class="file">
            <span class="label">File</span>
            @for (v of [fileVersion()]; track v) {
              <input type="file" [accept]="accept" [disabled]="uploading()" (change)="onFile($event)" aria-label="Document file" />
            }
            @if (fileError()) {
              <span class="error" role="alert">{{ fileError() }}</span>
            }
          </div>
          <app-button [disabled]="uploading()" (click)="upload()"><app-icon name="upload" [size]="16" />{{ uploading() ? 'Uploading…' : 'Upload' }}</app-button>
        </div>
        @if (typeOptions().length === 0) {
          <p class="hint">No document types for students are set up yet. Add them under School Setup → Document Types (applies to Student or Both).</p>
        }
        <p class="hint">PDF, PNG, JPG or WEBP · up to 5 MB.</p>
      } @else {
        <p class="hint">This application is closed, so documents can only be viewed and downloaded.</p>
      }

      @if (loading()) {
        <p class="hint" role="status">Loading documents…</p>
      } @else if (loadFailed()) {
        <p class="hint" role="alert">The documents could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p>
      } @else if (documents().length === 0) {
        <p class="hint">No documents uploaded yet.</p>
      } @else {
        <ul class="docs" aria-label="Documents">
          @for (d of documents(); track d.applicationDocumentId) {
            <li>
              <div class="info">
                <strong>{{ d.documentTypeName }}</strong>
                <span class="meta">{{ d.fileName }} · {{ size(d.fileSize) }} · {{ fmtDate(d.uploadedAt) }}{{ d.uploadedByName ? ' · ' + d.uploadedByName : '' }}</span>
                @if (d.remarks) {
                  <span class="meta">{{ d.remarks }}</span>
                }
              </div>
              <div class="row-actions">
                <app-button variant="secondary" size="sm" [disabled]="busyId() === d.applicationDocumentId" (click)="download(d)">Download</app-button>
                @if (!locked()) {
                  <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === d.applicationDocumentId" aria-label="Remove document" (click)="remove(d)"><app-icon name="trash-2" [size]="14" /></app-button>
                }
              </div>
            </li>
          }
        </ul>
      }
    </app-card>
  `,
})
export class AdmissionDocumentsComponent implements OnInit {
  private readonly service = inject(AdmissionService);
  private readonly types = inject(DocumentTypeService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  readonly admissionId = input.required<number>();
  /** True once the application is Rejected, Withdrawn or Admitted. */
  readonly locked = input(false);

  protected readonly accept = ADMISSION_DOCUMENT_ACCEPT;
  protected readonly fmtDate = formatDate;
  protected readonly size = formatFileSize;

  protected readonly documents = signal<AdmissionDocument[]>([]);
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
  protected readonly typeOptions = signal<SelectOption[]>([]);

  protected readonly typeOptionsReady = computed(() => this.typeOptions().length > 0);

  ngOnInit(): void {
    this.load();
    // a document type for an applicant: active, and meant for students (STUDENT) or for everyone (BOTH)
    this.types
      .list()
      .pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef))
      .subscribe((list) =>
        this.typeOptions.set(list.filter((t) => t.isActive && (t.appliesTo === 'STUDENT' || t.appliesTo === 'BOTH')).map((t) => ({ label: t.isRequired ? `${t.documentName} (required)` : t.documentName, value: String(t.documentTypeId) }))),
      );
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .documents(this.admissionId())
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (d) => this.documents.set(d), error: () => this.loadFailed.set(true) });
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
    else if (f.size > ADMISSION_DOCUMENT_MAX_BYTES) this.fileError.set('A document must be 5 MB or smaller.');
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
      .uploadDocument(this.admissionId(), this.file()!, Number(this.typeControl.value), this.remarksControl.value.trim() || null)
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
        error: () => undefined, // the server reason is toasted by the interceptor; the form keeps its values
      });
  }

  protected download(doc: AdmissionDocument): void {
    if (this.busyId() !== null) return;
    this.busyId.set(doc.applicationDocumentId);
    this.service
      .downloadDocument(this.admissionId(), doc.applicationDocumentId)
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

  protected async remove(doc: AdmissionDocument): Promise<void> {
    const ok = await this.confirmDialog.confirm({
      title: 'Remove document?',
      message: `"${doc.fileName}" (${doc.documentTypeName}) will be hidden from this application. The record is kept for the audit trail.`,
      confirmText: 'Remove',
      variant: 'danger',
    });
    if (!ok) return;
    this.busyId.set(doc.applicationDocumentId);
    this.service
      .removeDocument(this.admissionId(), doc.applicationDocumentId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.documents.update((l) => l.filter((d) => d.applicationDocumentId !== doc.applicationDocumentId));
          this.toast.success('Document removed.');
        },
        error: () => this.load(), // e.g. the application was closed meanwhile: show the truth
      });
  }
}
