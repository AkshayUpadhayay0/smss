import { ChangeDetectionStrategy, Component } from '@angular/core';

import { APP_IDENTITY, FOOTER_TEXT } from '../../core/config/app.constant';

@Component({
  selector: 'app-footer',
  standalone: true,
  template: './footer.component.html',
  styleUrl: './footer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FooterComponent {
  protected readonly copyright = FOOTER_TEXT;
  protected readonly identity = APP_IDENTITY;
}
