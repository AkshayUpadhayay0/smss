import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeaderComponent } from '../../../../shared/components';
import { StudentMyFeesComponent } from '../../components/student-my-fees/student-my-fees.component';

/** "My Fees" in the Student sidebar: the shared read-only fees component under a page header. */
@Component({
  selector: 'app-my-fees-page',
  imports: [PageHeaderComponent, StudentMyFeesComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header title="My Fees" subtitle="What is due, what you have paid, and the balance" icon="file-text" [breadcrumbs]="breadcrumbs" />
    <app-student-my-fees />
  `,
})
export class MyFeesPageComponent {
  protected readonly breadcrumbs = [{ label: 'My Fees' }];
}
