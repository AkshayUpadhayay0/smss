import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-progress-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './progress-bar.component.html',
  styleUrl: './progress-bar.component.scss',
})
export class ProgressBarComponent {
  readonly value = input<number>(0); // 0-100
  readonly label = input<string | undefined>(undefined);
  readonly variant = input<'primary' | 'success' | 'warning' | 'danger'>('primary');

  readonly clamped = computed(() => Math.min(100, Math.max(0, this.value())));
}
