import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ErrorPageComponent } from './error-page';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [ErrorPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-error-page code="403" icon="lock" title="Access restricted" message="You don't have permission to view this page. Contact your administrator if you think this is a mistake." color="var(--warning)" bg="var(--warning-light)" />`,
})
export class ForbiddenComponent {}
