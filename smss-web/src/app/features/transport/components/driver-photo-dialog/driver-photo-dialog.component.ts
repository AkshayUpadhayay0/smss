import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../core/services/toast.service';
import { ButtonComponent, ModalComponent } from '../../../../shared/components';
import { LogoChange, LogoUploadComponent } from '../../../schools/components/logo-upload/logo-upload.component';
import { DriverPhoto } from '../../models/transport.model';
import { TransportService } from '../../services/transport.service';

/** Adds, replaces or removes one driver's photo. Same picker as the employee / student photo (type, size and signature are checked there and again by the server). */
@Component({
  selector: 'app-driver-photo-dialog',
  imports: [ModalComponent, ButtonComponent, LogoUploadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal title="Driver photo" [dismissible]="!saving()" (closed)="closed.emit()">
      <p class="who">{{ driverName() }}</p>
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
export class DriverPhotoDialogComponent {
  private readonly service = inject(TransportService);
  private readonly toast = inject(ToastService);

  readonly driverId = input.required<string>();
  readonly driverName = input<string>('');
  /** The current photo path as the API returned it, or null. */
  readonly currentUrl = input<string | null>(null);
  readonly saved = output<DriverPhoto>();
  readonly closed = output<void>();

  protected readonly change = signal<LogoChange>({ file: null, remove: false });
  protected readonly saving = signal(false);

  protected hasChange(): boolean {
    const c = this.change();
    return !!c.file || c.remove;
  }

  protected save(): void {
    const { file, remove } = this.change();
    const request$ = file ? this.service.uploadDriverPhoto(this.driverId(), file) : remove ? this.service.removeDriverPhoto(this.driverId()) : null;
    if (!request$ || this.saving()) return;
    this.saving.set(true);
    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: (photo) => {
        this.toast.success(file ? 'Photo updated successfully.' : 'Photo removed.');
        this.saved.emit(photo);
      },
      error: () => undefined, // stays open so another file can be picked; the reason is toasted by the interceptor
    });
  }
}
