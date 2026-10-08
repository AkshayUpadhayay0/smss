import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService, ToastType } from '../../../core/services/toast.service';
import { IconComponent } from '../icon/icon.component';
import { IconName } from '../icon/icons';

const ICON_BY_TYPE: Record<ToastType, IconName> = {
  success: 'circle-check',
  error: 'circle-alert',
  info: 'info',
  warning: 'triangle-alert',
};

/** Mount once in the root component. */
@Component({
  selector: 'app-toast-container',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './toast-container.component.scss',
  template: `
    <div class="stack" aria-live="polite">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast" [class]="'toast ' + t.type" role="status">
          <app-icon [name]="icon(t.type)" [size]="20" />
          <div class="body">
            @if (t.title) {
              <strong>{{ t.title }}</strong>
            }
            <span>{{ t.message }}</span>
          </div>
          <button type="button" class="close" aria-label="Dismiss" (click)="toasts.dismiss(t.id)">
            <app-icon name="x" [size]="16" />
          </button>
        </div>
      }
    </div>
  `,
})
export class ToastContainerComponent {
  protected readonly toasts = inject(ToastService);
  protected icon(type: ToastType): IconName {
    return ICON_BY_TYPE[type];
  }
}
