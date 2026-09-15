import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

export type AlertVariant = 'success' | 'info' | 'warning' | 'danger';

const ICON_MAP: Record<AlertVariant, string> = {
  success: 'check-circle-2',
  info: 'info',
  warning: 'triangle-alert',
  danger: 'circle-alert',
};

@Component({
  selector: 'app-alert',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './alert.component.html',
  styleUrl: './alert.component.scss',
})
export class AlertComponent {
  readonly variant = input<AlertVariant>('info');
  readonly title = input<string | undefined>(undefined);
  readonly dismissible = input<boolean>(false);

  get icon(): string {
    return ICON_MAP[this.variant()];
  }
}
