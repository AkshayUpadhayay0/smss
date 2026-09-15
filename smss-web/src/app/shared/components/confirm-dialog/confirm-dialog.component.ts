import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ModalComponent } from '../modal/modal.component';
import { ButtonComponent } from '../button/button.component';
import { ConfirmDialogService } from '../../../core/services';

/**
 * Mounted once in AppComponent. Reads ConfirmDialogService.state() so any
 * component can call `confirmDialogService.confirm({...})` and await the
 * result without building its own confirmation modal (spec #11).
 */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [ModalComponent, ButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './confirm-dialog.component.html',
})
export class ConfirmDialogComponent {
  readonly confirmDialogService = inject(ConfirmDialogService);

  resolve(result: boolean): void {
    this.confirmDialogService.resolve(result);
  }
}
