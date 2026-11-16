import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CardComponent, PageHeaderComponent } from '../../../../shared/components';
import { InventoryTabsComponent } from '../../components/inventory-tabs/inventory-tabs.component';

/** A short static guide to how stock moves. No data, no requests. */
@Component({
  selector: 'app-inventory-guide',
  imports: [RouterLink, CardComponent, InventoryTabsComponent, PageHeaderComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../../styles/inventory-page.scss',
  templateUrl: './guide.component.html',
})
export class InventoryGuideComponent {
  protected readonly breadcrumbs = [{ label: 'Inventory' }, { label: 'Guide' }];
}
