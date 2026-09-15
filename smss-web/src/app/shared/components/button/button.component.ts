import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { SpinnerComponent } from '../spinner/spinner.component';

export type ButtonVariant = 'primary' | 'secondary' | 'outline' | 'ghost' | 'success' | 'warning' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

/**
 * Wraps the .btn utility classes (src/styles/_utilities.scss) so buttons
 * are still theme-aware everywhere, while giving call sites a typed API
 * for variant/size/loading/disabled instead of juggling class strings.
 */
@Component({
  selector: 'app-button',
  standalone: true,
  imports: [SpinnerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './button.component.html',
  styleUrl: './button.component.scss',
})
export class ButtonComponent {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  readonly loading = input<boolean>(false);
  readonly disabled = input<boolean>(false);
  readonly block = input<boolean>(false);

  readonly pressed = output<MouseEvent>();

  handleClick(event: MouseEvent): void {
    if (this.loading() || this.disabled()) return;
    this.pressed.emit(event);
  }
}
