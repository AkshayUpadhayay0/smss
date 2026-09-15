import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { SpinnerComponent } from '../spinner/spinner.component';
import { LoaderService } from '../../../core/services';

/**
 * Global full-page loading overlay, mounted once in AppComponent and
 * driven by LoaderService.isLoading(). Use for route/page-level
 * transitions; use app-spinner/app-skeleton locally for smaller areas.
 */
@Component({
  selector: 'app-page-loader',
  standalone: true,
  imports: [SpinnerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './loader.component.html',
  styleUrl: './loader.component.scss',
})
export class PageLoaderComponent {
  readonly loaderService = inject(LoaderService);
}
