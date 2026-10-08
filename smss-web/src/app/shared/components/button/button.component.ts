import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'danger-ghost';
export type ButtonSize = 'sm' | 'md' | 'lg';

/** Icon support: project an <app-icon> before the label — `<app-button><app-icon name="plus" />Add</app-button>` */
@Component({
  selector: 'app-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './button.component.scss',
  host: { '[class.block]': 'block()' },
  template: `
    <button [type]="type()" [disabled]="disabled()" [class]="'btn btn-' + variant() + ' btn-' + size()">
      <ng-content />
    </button>
  `,
})
export class ButtonComponent {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly type = input<'button' | 'submit'>('button');
  readonly disabled = input(false);
  readonly block = input(false);
}
