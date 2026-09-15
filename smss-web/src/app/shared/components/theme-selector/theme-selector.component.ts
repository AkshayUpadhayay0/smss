import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { IconComponent } from '../icon/icon.component';
import { ThemeService } from '../../../core/services';
import { ThemeName } from '../../../core/models';

@Component({
  selector: 'app-theme-selector',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './theme-selector.component.html',
  styleUrl: './theme-selector.component.scss',
})
export class ThemeSelectorComponent {
  readonly themeService = inject(ThemeService);

  readonly selected = input<ThemeName>('default');
  readonly themeSelected = output<ThemeName>();

  select(theme: ThemeName): void {
    this.themeSelected.emit(theme);
  }
}
