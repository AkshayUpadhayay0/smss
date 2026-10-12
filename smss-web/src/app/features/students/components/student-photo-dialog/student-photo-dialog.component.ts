import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, ModalComponent } from '../../../../shared/components';
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { StudentPhoto } from '../../models/student.model';
import { StudentService } from '../../services/student.service';

/**
 * Adds, replaces or removes one student's photo, independent of how the student was created.
 * Uses the same picker as the school logo (type / size / signature are checked there and again by the server).
 */
@Component({
  selector: 'app-student-photo-dialog',
  imports: [ModalComponent, ButtonComponent, LogoUploadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal title="Student photo" [dismissible]="!saving()" (closed)="closed.emit()">
      <p class="who">{{ studentName() }}</p>
      <app-logo-upload noun="photo" icon="user" [currentUrl]="currentUrl()" (changed)="change.set($event)" />

      <ng-container modal-footer>
        <app-button variant="secondary" [disabled]="saving()" (click)="closed.emit()">Cancel</app-button>
        <app-button [disabled]="saving() || !hasChange()" (click)="save()">{{ saving() ? 'Saving…' : 'Save photo' }}</app-button>
      </ng-container>
    </app-modal>
  `,
  styles: `
    .who {
      margin: 0 0 var(--space-4);
      font-weight: var(--weight-semibold);
    }
  `,
})
export class StudentPhotoDialogComponent {
  private readonly students = inject(StudentService);
  private readonly toast = inject(ToastService);

  readonly studentId = input.required<number>();
  readonly studentName = input<string>('');
  /** Absolute URL of the current photo, or null. */
  readonly currentUrl = input<string | null>(null);

  readonly saved = output<StudentPhoto>();
  readonly closed = output<void>();

  protected readonly change = signal<LogoChange>({ file: null, remove: false });
  protected readonly saving = signal(false);

  protected hasChange(): boolean {
    const c = this.change();
    return !!c.file || c.remove;
  }

  protected save(): void {
    const { file, remove } = this.change();
    const request$ = file ? this.students.uploadPhoto(this.studentId(), file) : remove ? this.students.removePhoto(this.studentId()) : null;
    if (!request$ || this.saving()) return;

    this.saving.set(true);
    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (photo) => {
        this.toast.success(file ? 'Photo updated successfully.' : 'Photo removed.');
        this.saved.emit(photo);
      },
      error: (e) => {
        // stays open so the user can pick another file
        const err = e as { error?: { message?: string }; message?: string };
        this.toast.error(err?.error?.message ?? err?.message ?? 'The photo could not be saved.');
      },
    });
  }
}
