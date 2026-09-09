import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ErrorPageComponent } from './error-page';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [ErrorPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-error-page code="404" icon="alert-triangle" title="Page not found" message="The page you're looking for doesn't exist or has been moved." color="var(--danger)" bg="var(--danger-light)" />`,
})
export class NotFoundComponent {}
