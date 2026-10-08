import { ChangeDetectionStrategy, Component, ElementRef, HostListener, afterNextRender, inject, input, output, viewChild } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

let nextId = 0;

/**
 * Generic modal. Content is projected; buttons go in the footer slot:
 * `<app-modal title="…" (closed)="…"> body <ng-container modal-footer> buttons </ng-container> </app-modal>`
 * Closes on backdrop click, Escape and the X button unless `dismissible` is false (e.g. while saving).
 */
@Component({
  selector: 'app-modal',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './modal.component.scss',
  template: `
    <div class="backdrop" (mousedown)="onBackdrop($event)">
      <div #dialog class="modal" role="dialog" aria-modal="true" tabindex="-1" [attr.aria-labelledby]="titleId">
        <header class="header">
          <h3 [id]="titleId">{{ title() }}</h3>
          <button type="button" class="close" aria-label="Close" [disabled]="!dismissible()" (click)="requestClose()">
            <app-icon name="x" [size]="18" />
          </button>
        </header>
        <div class="body"><ng-content /></div>
        <footer class="footer"><ng-content select="[modal-footer]" /></footer>
      </div>
    </div>
  `,
})
export class ModalComponent {
  protected readonly titleId = `app-modal-title-${nextId++}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLElement>>('dialog');

  readonly title = input.required<string>();
  readonly dismissible = input(true);
  readonly closed = output<void>();

  constructor() {
    // Move focus into the dialog unless a field inside already autofocused.
    afterNextRender(() => {
      const el = this.dialog().nativeElement;
      if (!el.contains(document.activeElement)) el.focus();
    });
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.requestClose();
  }

  protected onBackdrop(event: MouseEvent): void {
    // Only a press that starts on the backdrop itself closes (not a drag that ends there).
    if (event.target === event.currentTarget) this.requestClose();
  }

  protected requestClose(): void {
    if (this.dismissible()) this.closed.emit();
  }
}
