import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { canAccess } from '../../../../core/config/route-roles';
import { AuthService } from '../../../../core/services/auth.service';

interface InventoryTab { label: string; route: string }

/** Every inventory tab, in order. Which ones a person sees is decided by ROUTE_ROLES (the single source), so a Principal only gets Dashboard and Items. */
export const INVENTORY_TABS: readonly InventoryTab[] = [
  { label: 'Dashboard', route: '/inventory' },
  { label: 'Items / Categories', route: '/inventory/items' },
  { label: 'Add Stock', route: '/inventory/add-stock' },
  { label: 'Adjustments', route: '/inventory/adjustments' },
  { label: 'Issue / Return', route: '/inventory/issues' },
  { label: 'Suppliers', route: '/inventory/suppliers' },
  { label: 'Guide', route: '/inventory/guide' },
];

@Component({
  selector: 'app-inventory-tabs',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './inventory-tabs.component.scss',
  template: `
    <nav class="tabs" aria-label="Inventory sections">
      @for (tab of tabs(); track tab.route) {
        <a [routerLink]="tab.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">{{ tab.label }}</a>
      }
    </nav>
  `,
})
export class InventoryTabsComponent {
  private readonly auth = inject(AuthService);
  protected readonly tabs = computed(() => INVENTORY_TABS.filter((t) => canAccess(t.route, this.auth.currentUserRoles())));
}
