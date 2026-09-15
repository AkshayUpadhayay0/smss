import { ChangeDetectionStrategy, Component, HostListener, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

export type ModalSize = 'sm' | 'md' | 'lg' | 'fullscreen';

/**
 * Single reusable modal shell. Any page/component projects its own
 * content into it instead of re-implementing overlay/backdrop/close
 * behavior (spec #11 — "do not duplicate modal implementation").
 */
@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './modal.component.html',
  styleUrl: './modal.component.scss',
})
export class ModalComponent {
  readonly open = input<boolean>(false);
  readonly title = input<string | undefined>(undefined);
  readonly size = input<ModalSize>('md');
  readonly showClose = input<boolean>(true);
  readonly closeOnBackdrop = input<boolean>(true);

  readonly closed = output<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) this.close();
  }

  close(): void {
    this.closed.emit();
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop()) this.close();
  }
}
