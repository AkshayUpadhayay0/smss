import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, of, switchMap } from 'rxjs';
import { SecureImageService } from '../../../core/services/secure-image.service';
import { toUploadUrl } from '../../../core/utils/upload-url';

/**
 * An image that may need the login. Give it the path the API returned:
 *  - "/api/..." (a student photo): fetched with the Authorization header and shown from an object URL (cached, revoked on sign-out);
 *  - blob:, data:, http(s): shown as they are (a picked file's preview, a public school logo);
 *  - any other relative path (an old /uploads/... logo): made absolute.
 * While it is loading, or if it fails, the projected content (the initials, an icon) is shown instead.
 */
@Component({
  selector: 'app-secure-img',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (resolved(); as url) { <img [src]="url" [alt]="alt()" /> } @else { <ng-content /> }`,
  styles: `
    :host { display: block; width: 100%; height: 100%; border-radius: inherit; }
    img { display: block; width: 100%; height: 100%; object-fit: cover; border-radius: inherit; }
  `,
})
export class SecureImageComponent {
  private readonly images = inject(SecureImageService);

  readonly src = input<string | null | undefined>(null);
  readonly alt = input('');

  protected readonly resolved = toSignal(
    toObservable(this.src).pipe(switchMap((s): Observable<string | null> => {
      if (!s) return of(null);
      if (SecureImageService.isSecurePath(s)) return this.images.get(s);
      if (/^(blob:|data:|https?:)/i.test(s)) return of(s);
      return of(toUploadUrl(s));
    })),
    { initialValue: null },
  );
}
