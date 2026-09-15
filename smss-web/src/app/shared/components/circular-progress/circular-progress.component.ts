import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-circular-progress',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './circular-progress.component.html',
  styleUrl: './circular-progress.component.scss',
})
export class CircularProgressComponent {
  readonly value = input<number>(0); // 0-100
  readonly size = input<number>(72);
  readonly strokeWidth = input<number>(8);

  readonly clamped = computed(() => Math.min(100, Math.max(0, this.value())));
  readonly radius = computed(() => (this.size() - this.strokeWidth()) / 2);
  readonly circumference = computed(() => 2 * Math.PI * this.radius());
  readonly offset = computed(() => this.circumference() * (1 - this.clamped() / 100));
}
