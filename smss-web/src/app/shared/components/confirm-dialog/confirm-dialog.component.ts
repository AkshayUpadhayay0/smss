import { ChangeDetectionStrategy, Component, HostListener, inject } from '@angular/core';
import { ConfirmDialogService } from '../../../core/services/confirm-dialog.service';
import { ButtonComponent } from '../button/button.component';
import { IconComponent } from '../icon/icon.component';

/** Mount once in the root component; drive it through ConfirmDialogService.confirm(). */
@Component({
  selector: 'app-confirm-dialog',
  imports: [ButtonComponent, IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './confirm-dialog.component.scss',
  template: `
    @if (dialog.state(); as s) {
      <div class="backdrop" (click)="dialog.resolve(false)">
        <div class="modal" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" (click)="$event.stopPropagation()">
          <span class="badge" [class.danger]="s.variant === 'danger'">
            <app-icon [name]="s.variant === 'danger' ? 'triangle-alert' : 'info'" [size]="22" />
          </span>
          <h3 id="confirm-title">{{ s.title }}</h3>
          <p>{{ s.message }}</p>
          <div class="buttons">
            <app-button variant="secondary" (click)="dialog.resolve(false)">{{ s.cancelText }}</app-button>
            <app-button [variant]="s.variant === 'danger' ? 'danger' : 'primary'" (click)="dialog.resolve(true)">
              {{ s.confirmText }}
            </app-button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmDialogComponent {
  protected readonly dialog = inject(ConfirmDialogService);

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (this.dialog.state()) this.dialog.resolve(false);
  }
}
