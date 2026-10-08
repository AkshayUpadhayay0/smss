import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ICONS, IconName, IconShape } from './icons';

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './icon.component.scss',
  template: `
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      [attr.width]="size()"
      [attr.height]="size()"
    >
      @for (shape of shapes(); track $index) {
        @if (isPath(shape)) {
          <path [attr.d]="shape" />
        } @else if (isCircle(shape)) {
          <circle [attr.cx]="shape.c[0]" [attr.cy]="shape.c[1]" [attr.r]="shape.c[2]" />
        } @else {
          <rect [attr.x]="shape.r[0]" [attr.y]="shape.r[1]" [attr.width]="shape.r[2]" [attr.height]="shape.r[3]" [attr.rx]="shape.r[4]" />
        }
      }
    </svg>
  `,
})
export class IconComponent {
  readonly name = input.required<IconName>();
  readonly size = input(18);

  protected readonly shapes = computed<readonly IconShape[]>(() => ICONS[this.name()] ?? []);

  protected isPath(s: IconShape): s is string {
    return typeof s === 'string';
  }
  protected isCircle(s: IconShape): s is { c: [number, number, number] } {
    return typeof s !== 'string' && 'c' in s;
  }
}
