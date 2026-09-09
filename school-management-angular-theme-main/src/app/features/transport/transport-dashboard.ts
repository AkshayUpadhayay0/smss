import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge';
import { TabsComponent, TabItem } from '../../shared/components/tabs/tabs';
import { TransportService } from '../../core/services/data.service';
import { Vehicle, TransportRoute } from '../../core/models/school.models';

@Component({
  selector: 'app-transport-dashboard',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, StatusBadgeComponent, TabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './transport-dashboard.html',
})
export class TransportDashboardComponent {
  private transportService = inject(TransportService);
  vehicles = signal<Vehicle[]>([]);
  routes = signal<TransportRoute[]>([]);
  activeTab = signal('routes');

  tabs: TabItem[] = [
    { id: 'routes', label: 'Routes', icon: 'map-pin' },
    { id: 'vehicles', label: 'Vehicles', icon: 'bus' },
    { id: 'drivers', label: 'Drivers', icon: 'user-check' },
  ];

  constructor() {
    this.transportService.getVehicles().subscribe(list => this.vehicles.set(list));
    this.transportService.getRoutes().subscribe(list => this.routes.set(list));
  }

  totalStudents = computed(() => this.routes().reduce((a, r) => a + r.studentCount, 0));
}
