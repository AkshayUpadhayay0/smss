import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header';
import { IconComponent } from '../../shared/components/icon/icon';
import { ProgressBarComponent } from '../../shared/components/progress-bar/progress-bar';
import { HostelService } from '../../core/services/data.service';
import { HostelRoom } from '../../core/models/school.models';

@Component({
  selector: 'app-hostel-dashboard',
  standalone: true,
  imports: [CommonModule, PageHeaderComponent, IconComponent, ProgressBarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './hostel-dashboard.html',
})
export class HostelDashboardComponent {
  private hostelService = inject(HostelService);
  rooms = signal<HostelRoom[]>([]);

  constructor() {
    this.hostelService.getRooms().subscribe(list => this.rooms.set(list));
  }

  stats = computed(() => {
    const rooms = this.rooms();
    const totalCapacity = rooms.reduce((a, r) => a + r.capacity, 0);
    const occupied = rooms.reduce((a, r) => a + r.occupied, 0);
    return { totalRooms: rooms.length, occupied, available: totalCapacity - occupied, pct: totalCapacity ? Math.round((occupied / totalCapacity) * 100) : 0 };
  });

  buildings = computed(() => Array.from(new Set(this.rooms().map(r => r.building))));

  roomsFor(building: string): HostelRoom[] {
    return this.rooms().filter(r => r.building === building);
  }
}
