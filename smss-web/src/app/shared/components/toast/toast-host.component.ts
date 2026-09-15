import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { IconComponent } from '../icon/icon.component';
import { ToastService } from '../../../core/services';
import { ToastVariant } from '../../../core/models';

const ICON_MAP: Record<ToastVariant, string> = {
  success: 'check-circle-2',
  info: 'info',
  warning: 'triangle-alert',
  danger: 'circle-alert',
};

/**
 * Mounted once in AppComponent. Renders whatever ToastService.toasts()
 * currently holds — no page needs to render its own toast markup.
 */
@Component({
  selector: 'app-toast-host',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast-host.component.html',
  styleUrl: './toast-host.component.scss',
})
export class ToastHostComponent {
  readonly toastService = inject(ToastService);

  iconFor(variant: ToastVariant): string {
    return ICON_MAP[variant];
  }

  dismiss(id: number): void {
    this.toastService.dismiss(id);
  }
}
