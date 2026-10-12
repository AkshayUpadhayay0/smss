import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Observable, catchError, finalize, map, of, switchMap } from 'rxjs';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, IconComponent, ModalComponent, SelectComponent, SelectOption, TextareaComponent, InputComponent } from '../../../../shared/components';
import { SectionService } from '../../../school-setup/services/section.service';
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { StudentService } from '../../../students/services/student.service';
import { ACTIONS_BY_STATUS, Admission, AdmissionAction, AdmissionDetail } from '../../models/admission.model';
import { AdmissionService } from '../../services/admission.service';

interface ActionView {
  action: AdmissionAction;
  label: string;
  icon: 'eye' | 'check' | 'x' | 'circle-check' | 'user';
  variant: 'primary' | 'secondary' | 'danger-ghost';
}

const ACTION_VIEWS: Record<AdmissionAction, ActionView> = {
  'start-review': { action: 'start-review', label: 'Start Review', icon: 'eye', variant: 'secondary' },
  approve: { action: 'approve', label: 'Approve', icon: 'check', variant: 'primary' },
  reject: { action: 'reject', label: 'Reject', icon: 'x', variant: 'danger-ghost' },
  cancel: { action: 'cancel', label: 'Cancel', icon: 'x', variant: 'danger-ghost' },
  confirm: { action: 'confirm', label: 'Confirm Admission', icon: 'circle-check', variant: 'primary' },
};

/**
 * The workflow buttons valid for one application's current status, plus the dialogs they need.
 * Used by the list rows and the detail page, so the rules live in one place (ACTIONS_BY_STATUS).
 */
@Component({
  selector: 'app-admission-actions',
  imports: [ReactiveFormsModule, ButtonComponent, IconComponent, InputComponent, LogoUploadComponent, ModalComponent, SelectComponent, TextareaComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './admission-actions.component.scss',
  templateUrl: './admission-actions.component.html',
})
export class AdmissionActionsComponent {
  private readonly service = inject(AdmissionService);
  private readonly sections = inject(SectionService);
  private readonly studentApi = inject(StudentService);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly toast = inject(ToastService);

  readonly admission = input.required<Admission>();
  /** Emits the updated application after any successful action. */
  readonly changed = output<AdmissionDetail>();

  protected readonly views = computed(() => (ACTIONS_BY_STATUS[this.admission().statusName] ?? []).map((a) => ACTION_VIEWS[a]));

  protected readonly busy = signal(false);
  /** Which dialog is open (approve / reject / cancel / confirm); start-review uses the confirm service instead. */
  protected readonly dialog = signal<AdmissionAction | null>(null);
  protected readonly remarks = new FormControl('', { nonNullable: true });
  protected readonly remarksError = signal<string | undefined>(undefined);

  // confirm-admission dialog
  protected readonly sectionControl = new FormControl('', { nonNullable: true });
  protected readonly rollNumber = new FormControl('', { nonNullable: true });
  protected readonly sectionOptions = signal<SelectOption[]>([]);
  protected readonly sectionsLoading = signal(false);
  protected readonly sectionError = signal<string | undefined>(undefined);
  /** Optional photo, uploaded right after the admission is confirmed (the student exists only then). */
  private photo: File | null = null;

  protected readonly dialogTitle = computed(() => {
    switch (this.dialog()) {
      case 'approve': return 'Approve application';
      case 'reject': return 'Reject application';
      case 'cancel': return 'Cancel application';
      case 'confirm': return 'Confirm admission';
      default: return '';
    }
  });

  protected run(action: AdmissionAction): void {
    if (this.busy()) return;
    if (action === 'start-review') {
      void this.startReview();
      return;
    }
    this.remarks.reset('');
    this.remarksError.set(undefined);
    this.sectionError.set(undefined);
    if (action === 'confirm') this.openConfirm();
    this.dialog.set(action);
  }

  private async startReview(): Promise<void> {
    const a = this.admission();
    const ok = await this.confirmDialog.confirm({
      title: 'Start review?',
      message: `Move application ${a.applicationNumber} (${a.applicantFullName}) to Under Review?`,
      confirmText: 'Start Review',
    });
    if (ok) this.send(this.service.startReview(a.admissionId), 'Application moved to review.');
  }

  /** Sections of the class the student applied for (active only), loaded when the dialog opens. */
  private openConfirm(): void {
    this.sectionControl.reset('');
    this.rollNumber.reset('');
    this.photo = null;
    this.sectionOptions.set([]);
    this.sectionsLoading.set(true);
    const classId = this.admission().appliedClassId;
    this.sections
      .list()
      .pipe(
        catchError(() => of([])), // already toasted by the interceptor
        finalize(() => this.sectionsLoading.set(false)),
      )
      .subscribe((list) =>
        this.sectionOptions.set(
          list.filter((s) => s.classId === classId && s.statusName === 'Active').map((s) => ({ label: `${s.className} – ${s.sectionName}`, value: String(s.sectionId) })),
        ),
      );
  }

  protected onPhotoChanged(change: LogoChange): void {
    this.photo = change.file;
  }

  protected closeDialog(): void {
    if (!this.busy()) this.dialog.set(null);
  }

  protected submitDialog(): void {
    const action = this.dialog();
    const a = this.admission();
    if (!action || this.busy()) return;

    const text = this.remarks.value.trim();
    if (action === 'confirm') {
      if (!this.sectionControl.value) {
        this.sectionError.set('Select a section');
        return;
      }
      const photo = this.photo;
      this.send(
        this.service.confirmAdmission(a.admissionId, { classSectionId: Number(this.sectionControl.value), rollNumber: this.rollNumber.value.trim() || null }).pipe(
          // the admission is confirmed first; the photo is a best-effort follow-up that must never undo or fail that
          switchMap((updated) =>
            photo && updated.studentId
              ? this.studentApi.uploadPhoto(updated.studentId, photo).pipe(
                  map(() => updated),
                  catchError((e) => {
                    const err = e as { error?: { message?: string }; message?: string };
                    this.toast.warning(
                      `${err?.error?.message ?? err?.message ?? 'The photo could not be uploaded.'} You can add it later from the student's profile.`,
                      'Admission confirmed, but the photo was not uploaded',
                    );
                    return of(updated);
                  }),
                )
              : of(updated),
          ),
        ),
        'Admission confirmed. The student has been created.',
      );
    } else if (action === 'reject') {
      if (!text) {
        this.remarksError.set('A reason is required to reject an application');
        return;
      }
      this.send(this.service.reject(a.admissionId, text), 'Application rejected.');
    } else if (action === 'approve') {
      this.send(this.service.approve(a.admissionId, text || null), 'Application approved.');
    } else if (action === 'cancel') {
      this.send(this.service.cancel(a.admissionId, text || null), 'Application cancelled.');
    }
  }

  private send(request$: Observable<AdmissionDetail>, successMessage: string): void {
    this.busy.set(true);
    request$.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: (updated) => {
        this.dialog.set(null);
        this.toast.success(successMessage);
        this.changed.emit(updated);
      },
      // The server's refusal (e.g. the status changed meanwhile) is already toasted by the interceptor
      error: () => undefined,
    });
  }
}
