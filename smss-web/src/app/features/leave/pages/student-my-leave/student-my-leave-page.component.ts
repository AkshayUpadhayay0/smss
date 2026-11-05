import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeaderComponent } from '../../../../shared/components';
import { StudentMyLeaveComponent } from '../../components/student-my-leave/student-my-leave.component';

/** "My Leave" in the Student sidebar: the shared own-leave component under a page header. */
@Component({
  selector: 'app-student-my-leave-page',
  imports: [PageHeaderComponent, StudentMyLeaveComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="My Leave" subtitle="Ask for leave and follow your requests" icon="calendar" [breadcrumbs]="breadcrumbs" />
    <app-student-my-leave />
  `,
})
export class StudentMyLeavePageComponent {
  protected readonly breadcrumbs = [{ label: 'My Leave' }];
}
