import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, finalize, of } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, CardComponent, IconComponent, InputComponent, SelectComponent, SelectOption } from '../../../../shared/components';
import { formatFileSize } from '../../../employees/models/employee.model';
import { DocumentTypeService } from '../../../school-setup/services/document-type.service';
import {
  DOCUMENT_STATUS_LABEL,
  RequiredDocumentStatus,
  STUDENT_DOCUMENT_ACCEPT,
  STUDENT_DOCUMENT_MAX_BYTES,
  StudentDocument,
  StudentDocuments,
} from '../../models/student-document.model';
import { StudentDocumentService } from '../../services/student-document.service';

const ALLOWED = ['.pdf', '.png', '.jpg', '.jpeg', '.webp'];

/**
 * A student's documents: the required types with a status chip each (PENDING shows as "Document not submitted"), and every active document. School Admin, Principal and Admission Staff can
 * upload, waive (a reason is required) and void; the class teacher only sees the statuses. Files are private: they are fetched through the authenticated API, never by URL. When the
 * server refuses (a teacher who is not this section's class teacher) the card hides itself without a toast. Documents never block anything.
 */
@Component({
  selector: 'app-student-documents',
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, IconComponent, InputComponent, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './student-documents.component.scss',
  template: `
    @if (!hidden()) {
      <app-card title="Documents">
        @if (loading()) {
          <p class="hint" role="status">Loading documents…</p>
        } @else if (loadFailed()) {
          <p class="hint" role="alert">The documents could not be loaded. <app-button variant="secondary" size="sm" (click)="load()">Try again</app-button></p>
        } @else if (data(); as d) {
          @if (d.required.length > 0) {
            <ul class="required" aria-label="Required documents" data-list="required">
              @for (r of d.required; track r.documentTypeId) {
                <li [attr.data-status]="r.status">
                  <span class="name">{{ r.documentTypeName }}</span>
                  <span class="chip" [class]="'chip ' + r.status.toLowerCase()" data-chip>{{ label(r.status) }}</span>
                  @if (d.canManage && r.status === 'PENDING') {
                    <app-button variant="secondary" size="sm" (click)="startWaive(r.documentTypeId)">Waive</app-button>
                  }
                </li>
                @if (waiveFor() === r.documentTypeId) {
                  <li class="waive" data-form="waive">
                    <app-input label="Reason for waiving" [required]="true" placeholder="Required" [formControl]="reasonControl" [errorText]="reasonError()" />
                    <app-button [disabled]="busy()" (click)="confirmWaive(r.documentTypeId)">Waive document</app-button>
                    <app-button variant="secondary" [disabled]="busy()" (click)="waiveFor.set(null)">Cancel</app-button>
                  </li>
                }
              }
            </ul>
          } @else {
            <p class="hint" data-note="none-required">No document is required for this student.</p>
          }

          @if (d.canManage) {
            <div class="upload" data-form="upload">
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
            <p class="hint">PDF, PNG, JPG or WEBP · up to 5 MB. A new file replaces the current one of the same type.</p>
          }

          @if (d.documents.length > 0) {
            <ul class="docs" aria-label="Documents" data-list="documents">
              @for (doc of d.documents; track doc.studentDocumentId) {
                <li>
                  <div class="info">
                    <strong>{{ doc.documentTypeName }}</strong>
                    @if (doc.status === 'WAIVED') {
                      <span class="meta">Waived{{ doc.waiverReason ? ': ' + doc.waiverReason : '' }}{{ doc.byName ? ' · ' + doc.byName : '' }}</span>
                    } @else if (doc.fileName) {
                      <span class="meta">{{ doc.fileName }} · {{ size(doc.fileSize ?? 0) }}{{ doc.byName ? ' · ' + doc.byName : '' }}{{ doc.fromApplication ? ' · from the application' : '' }}</span>
                    } @else {
                      <span class="meta">{{ label(doc.status) }}</span>
                    }
                    @if (doc.remarks) {
                      <span class="meta">{{ doc.remarks }}</span>
                    }
                  </div>
                  @if (d.canManage) {
                    <div class="row-actions">
                      @if (doc.status === 'SUBMITTED') {
                        <app-button variant="secondary" size="sm" [disabled]="busyId() === doc.studentDocumentId" (click)="download(doc)">Download</app-button>
                      }
                      <app-button variant="danger-ghost" size="sm" [disabled]="busyId() === doc.studentDocumentId" [attr.aria-label]="doc.status === 'WAIVED' ? 'Remove waiver' : 'Void document'" (click)="voidIt(doc)"><app-icon name="trash-2" [size]="14" /></app-button>
                    </div>
                  }
                </li>
              }
            </ul>
          } @else {
            <p class="hint">No documents on file yet.</p>
          }
        }
      </app-card>
    }
  `,
})
export class StudentDocumentsComponent implements OnInit {
  private readonly service = inject(StudentDocumentService);
  private readonly types = inject(DocumentTypeService);
  private readonly toast = inject(ToastService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly destroyRef = inject(DestroyRef);

  readonly studentId = input.required<string>();
  /** Emits the number of required documents still pending after every load or change (the pending list uses it to refresh). */
  readonly changed = output<number>();

  protected readonly accept = STUDENT_DOCUMENT_ACCEPT;
  protected readonly size = formatFileSize;

  protected readonly data = signal<StudentDocuments | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  /** True when the server refused (403): the card is not shown at all. */
  protected readonly hidden = signal(false);
  protected readonly uploading = signal(false);
  protected readonly busy = signal(false);
  protected readonly busyId = signal<number | null>(null);
  protected readonly waiveFor = signal<number | null>(null);

  protected readonly typeControl = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly remarksControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  protected readonly reasonControl = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | undefined>(undefined);
  protected readonly typeError = signal<string | undefined>(undefined);
  protected readonly reasonError = signal<string | undefined>(undefined);
  protected readonly fileVersion = signal(0);
  private readonly allTypes = signal<SelectOption[]>([]);
  /** Types that still need a file come first. */
  protected readonly typeOptions = computed<SelectOption[]>(() => {
    const pending = new Set((this.data()?.required ?? []).filter((r) => r.status === 'PENDING').map((r) => String(r.documentTypeId)));
    return [...this.allTypes()].sort((a, b) => Number(pending.has(b.value)) - Number(pending.has(a.value)));
  });

  protected label(status: RequiredDocumentStatus | 'SUBMITTED' | 'WAIVED'): string {
    return DOCUMENT_STATUS_LABEL[status];
  }

  ngOnInit(): void {
    this.load();
    // the types a document can be filed under: active, and meant for students (STUDENT) or for everyone (BOTH)
    this.types
      .list()
      .pipe(catchError(() => of([])), takeUntilDestroyed(this.destroyRef))
      .subscribe((list) =>
        this.allTypes.set((list ?? []).filter((t) => t.isActive && (t.appliesTo === 'STUDENT' || t.appliesTo === 'BOTH')).map((t) => ({ label: t.isRequired ? `${t.documentName} (required)` : t.documentName, value: String(t.documentTypeId) }))),
      );
  }

  protected load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.service
      .get(this.studentId(), true)
      .pipe(finalize(() => this.loading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          if (!d) return;
          this.data.set(d);
          this.changed.emit(d.pendingCount);
        },
        error: (e: { status?: number }) => {
          if (e?.status === 403) this.hidden.set(true);
          else this.loadFailed.set(true);
        },
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
    else if (f.size > STUDENT_DOCUMENT_MAX_BYTES) this.fileError.set('A document must be 5 MB or smaller.');
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
      .upload(this.studentId(), this.file()!, Number(this.typeControl.value), this.remarksControl.value.trim() || null)
      .pipe(finalize(() => this.uploading.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Document uploaded.');
          this.typeControl.reset('');
          this.remarksControl.reset('');
          this.file.set(null);
          this.fileVersion.update((v) => v + 1);   // a fresh file input
          this.load();
        },
        error: () => undefined,   // the server reason is toasted by the interceptor; the form keeps its values
      });
  }

  protected startWaive(typeId: number): void {
    this.reasonControl.reset('');
    this.reasonError.set(undefined);
    this.waiveFor.set(typeId);
  }

  protected confirmWaive(typeId: number): void {
    if (this.busy()) return;
    const reason = this.reasonControl.value.trim();
    if (!reason) {
      this.reasonError.set('A reason is required to waive a document');
      return;
    }
    this.busy.set(true);
    this.service
      .waive(this.studentId(), typeId, reason)
      .pipe(finalize(() => this.busy.set(false)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Document waived.');
          this.waiveFor.set(null);
          this.load();
        },
        error: () => this.load(),   // e.g. someone uploaded it meanwhile: show the truth
      });
  }

  protected download(doc: StudentDocument): void {
    if (this.busyId() !== null || !doc.fileName) return;
    this.busyId.set(doc.studentDocumentId);
    this.service
      .download(this.studentId(), doc.studentDocumentId)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = doc.fileName!;
          a.click();
          URL.revokeObjectURL(url);
        },
        error: () => undefined,   // already toasted by the interceptor
      });
  }

  protected async voidIt(doc: StudentDocument): Promise<void> {
    const waiver = doc.status === 'WAIVED';
    const ok = await this.confirmDialog.confirm({
      title: waiver ? 'Remove waiver?' : 'Void document?',
      message: waiver
        ? `The waiver of "${doc.documentTypeName}" will be removed. The record is kept for the audit trail.`
        : `"${doc.fileName}" (${doc.documentTypeName}) will be hidden from this student. The file and the record are kept for the audit trail.`,
      confirmText: waiver ? 'Remove waiver' : 'Void',
      variant: 'danger',
    });
    if (!ok) return;
    this.busyId.set(doc.studentDocumentId);
    this.service
      .voidDocument(this.studentId(), doc.studentDocumentId, null)
      .pipe(finalize(() => this.busyId.set(null)), takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(waiver ? 'Waiver removed.' : 'Document voided.');
          this.load();
        },
        error: () => this.load(),
      });
  }
}
