import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CardComponent } from '../../shared/components/card/card.component';
import { ButtonComponent } from '../../shared/components/button/button.component';
import { ModalComponent } from '../../shared/components/modal/modal.component';
import { InputComponent } from '../../shared/components/input/input.component';
import { SpinnerComponent } from '../../shared/components/spinner/spinner.component';
import { ConfirmDialogService, ToastService } from '../../core/services';

type ModalKey = 'basic' | 'form' | 'large' | 'fullscreen' | 'loading' | null;

@Component({
  selector: 'app-modals-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    CardComponent,
    ButtonComponent,
    ModalComponent,
    InputComponent,
    SpinnerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './modals.component.html',
  styleUrl: './modals.component.scss',
})
export class ModalsPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly confirmDialogService = inject(ConfirmDialogService);
  private readonly toastService = inject(ToastService);

  readonly activeModal = signal<ModalKey>(null);

  readonly quickForm = this.fb.group({
    title: ['', Validators.required],
    description: [''],
  });

  open(key: Exclude<ModalKey, null>): void {
    this.activeModal.set(key);
  }

  close(): void {
    this.activeModal.set(null);
  }

  async openConfirm(): Promise<void> {
    const confirmed = await this.confirmDialogService.confirm({
      title: 'Delete this record?',
      message: 'Are you sure you want to delete this record? This action cannot be undone.',
      confirmLabel: 'Delete',
      variant: 'danger',
    });

    this.toastService.show(confirmed ? 'success' : 'info', confirmed ? 'Record deleted' : 'Cancelled', confirmed ? 'The record was removed.' : 'No changes were made.');
  }

  submitQuickForm(): void {
    this.quickForm.markAllAsTouched();
    if (this.quickForm.invalid) return;
    this.toastService.success('Saved', 'Your entry was saved (mock submission).');
    this.quickForm.reset();
    this.close();
  }

  simulateLoadingModal(): void {
    this.open('loading');
    setTimeout(() => this.close(), 2000);
  }
}
