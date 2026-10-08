import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'app-logo',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './logo.component.scss',
  template: `
    <span class="mark"><app-icon name="graduation-cap" [size]="20" /></span>
    @if (showText()) {
      <span class="wordmark">SMSS</span>
    }
  `,
})
export class LogoComponent {
  readonly showText = input(true);
}
