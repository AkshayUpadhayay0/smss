import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ErrorPageComponent } from './error-page';

@Component({
  selector: 'app-maintenance',
  standalone: true,
  imports: [ErrorPageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-error-page code="Maintenance" icon="sliders" title="Scheduled maintenance" message="We're performing scheduled maintenance to improve your experience. We'll be back online shortly." color="var(--info)" bg="var(--info-light)" />`,
})
export class MaintenanceComponent {}
