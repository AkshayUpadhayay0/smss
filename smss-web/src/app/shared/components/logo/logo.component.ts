import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './logo.component.scss',
  template: `
    <img class="mark" src="smss-icon.svg" alt="SMSS logo" width="40" height="40" />
    @if (showText()) {
      <span class="wordmark">SMSS</span>
    }
  `,
})
export class LogoComponent {
  readonly showText = input(true);
}
