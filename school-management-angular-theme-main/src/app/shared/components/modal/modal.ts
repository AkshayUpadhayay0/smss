import { ChangeDetectionStrategy, Component, HostListener, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon';

@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div class="backdrop" (click)="close.emit()">
        <div class="modal" [class.sm]="size() === 'sm'" [class.lg]="size() === 'lg'" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>{{ title() }}</h3>
            <button class="btn btn-ghost btn-icon btn-sm" (click)="close.emit()" aria-label="Close dialog">
              <app-icon name="x" [size]="18" />
            </button>
          </div>
          <div class="modal-body">
            <ng-content></ng-content>
          </div>
          @if (showFooter()) {
            <div class="modal-footer">
              <ng-content select="[footer]"></ng-content>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    .backdrop {
      position: fixed; inset: 0; background: rgba(10, 14, 24, 0.55); backdrop-filter: blur(2px);
      display: flex; align-items: center; justify-content: center; z-index: 1000; padding: var(--space-4);
      animation: fadeSlideIn 160ms ease;
    }
    .modal {
      background: var(--surface); border-radius: var(--radius-lg); box-shadow: var(--shadow-lg);
      width: 100%; max-width: 560px; max-height: 88vh; display: flex; flex-direction: column; overflow: hidden;
    }
    .modal.sm { max-width: 400px; }
    .modal.lg { max-width: 820px; }
    .modal-header { display: flex; align-items: center; justify-content: space-between; padding: var(--space-5) var(--space-6); border-bottom: 1px solid var(--border); }
    .modal-header h3 { font-size: 16px; }
    .modal-body { padding: var(--space-6); overflow-y: auto; }
    .modal-footer { padding: var(--space-4) var(--space-6); border-top: 1px solid var(--border); display: flex; justify-content: flex-end; gap: var(--space-2); }
  `],
})
export class ModalComponent {
  open = input<boolean>(false);
  title = input<string>('');
  size = input<'sm' | 'md' | 'lg'>('md');
  showFooter = input<boolean>(true);
  close = output<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) this.close.emit();
  }
}
