import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';

/**
 * Single wrapper around the icon library. Every other component renders
 * icons through <app-icon> instead of importing lucide directly, so the
 * whole app is guaranteed to use one consistent icon system (spec #32).
 */
@Component({
  selector: 'app-icon',
  standalone: true,
  imports: [LucideDynamicIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<svg [lucideIcon]="name()" [size]="size()" [strokeWidth]="strokeWidth()" class="app-icon"></svg>`,
  styles: [
    `
      :host {
        display: inline-flex;
        line-height: 0;
      }
      .app-icon {
        display: block;
      }
    `,
  ],
})
export class IconComponent {
  readonly name = input.required<string>();
  readonly size = input<number>(20);
  readonly strokeWidth = input<number>(1.9);
}
