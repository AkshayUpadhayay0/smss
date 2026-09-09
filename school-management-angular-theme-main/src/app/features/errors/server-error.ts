import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ErrorPageComponent } from './error-page';

@Component({
  selector: 'app-server-error',
  standalone: true,
  imports: [ErrorPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-error-page code="500" icon="zap" title="Something went wrong" message="An unexpected error occurred on our end. Please try again in a moment." color="var(--danger)" bg="var(--danger-light)" />`,
})
export class ServerErrorComponent {}
