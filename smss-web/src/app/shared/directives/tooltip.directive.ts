import {
  Directive,
  ElementRef,
  HostListener,
  Renderer2,
  inject,
  input,
} from '@angular/core';

/**
 * Lightweight tooltip directive — `<button appTooltip="Delete user">`.
 * Avoids pulling in a full overlay/positioning library for a simple hint.
 */
@Directive({
  selector: '[appTooltip]',
  standalone: true,
})
export class TooltipDirective {
  readonly appTooltip = input<string>('');
  readonly tooltipPosition = input<'top' | 'bottom'>('top');

  private readonly el = inject(ElementRef<HTMLElement>);
  private readonly renderer = inject(Renderer2);
  private tooltipEl: HTMLElement | null = null;

  @HostListener('mouseenter')
  @HostListener('focus')
  show(): void {
    if (!this.appTooltip() || this.tooltipEl) return;

    const tooltip = this.renderer.createElement('span') as HTMLElement;
    this.renderer.addClass(tooltip, 'app-tooltip-bubble');
    this.renderer.addClass(tooltip, `is-${this.tooltipPosition()}`);
    const text = this.renderer.createText(this.appTooltip());
    this.renderer.appendChild(tooltip, text);
    this.renderer.appendChild(this.el.nativeElement, tooltip);
    this.renderer.setStyle(this.el.nativeElement, 'position', 'relative');
    this.tooltipEl = tooltip;
  }

  @HostListener('mouseleave')
  @HostListener('blur')
  hide(): void {
    if (this.tooltipEl) {
      this.renderer.removeChild(this.el.nativeElement, this.tooltipEl);
      this.tooltipEl = null;
    }
  }
}
