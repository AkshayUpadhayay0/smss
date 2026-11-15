import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { SecureImageComponent } from './secure-image.component';

const API = environment.apiUrl;

@Component({
  imports: [SecureImageComponent],
  template: `<app-secure-img [src]="src()" alt="Asha"><span class="fallback">AK</span></app-secure-img>`,
})
class HostComponent { src = signal<string | null>(null); }

describe('SecureImageComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:t/${++n}`);
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HostComponent);
  });
  afterEach(() => { http.verify(); vi.restoreAllMocks(); });

  const settle = async () => { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); };

  it('shows the projected fallback when there is no source', async () => {
    await settle();
    expect(el().querySelector('img')).toBeNull();
    expect(el().querySelector('.fallback')?.textContent).toBe('AK');
  });

  it('an API path is fetched with the login (no img src is ever the API path) and shown from an object URL', async () => {
    fixture.componentInstance.src.set('/api/Student/S1/photo?v=1');
    await settle();
    expect(el().querySelector('img')).toBeNull();                         // still loading: fallback, and no <img src="/api/..."> that would 401
    http.expectOne(`${API}/api/Student/S1/photo?v=1`).flush(new Blob(['x']));
    await settle();
    const img = el().querySelector('img')!;
    expect(img.getAttribute('src')).toBe('blob:t/1');
    expect(img.getAttribute('alt')).toBe('Asha');
    expect(el().querySelector('.fallback')).toBeNull();
  });

  it('keeps the fallback when the image cannot be loaded', async () => {
    fixture.componentInstance.src.set('/api/Student/S1/photo?v=1');
    await settle();
    http.expectOne(`${API}/api/Student/S1/photo?v=1`).flush(new Blob(['no']), { status: 404, statusText: 'Not Found' });
    await settle();
    expect(el().querySelector('img')).toBeNull();
    expect(el().querySelector('.fallback')).not.toBeNull();
  });

  it('a picked file preview (blob:) and a public URL are shown as they are, with no request', async () => {
    fixture.componentInstance.src.set('blob:preview/1');
    await settle();
    expect(el().querySelector('img')!.getAttribute('src')).toBe('blob:preview/1');
    fixture.componentInstance.src.set('https://cdn.example/logo.png');
    await settle();
    expect(el().querySelector('img')!.getAttribute('src')).toBe('https://cdn.example/logo.png');
  });

  it('a changed source loads the new image', async () => {
    fixture.componentInstance.src.set('/api/Student/S1/photo?v=1');
    await settle();
    http.expectOne(`${API}/api/Student/S1/photo?v=1`).flush(new Blob(['a']));
    await settle();
    fixture.componentInstance.src.set('/api/Student/S1/photo?v=2');
    await settle();
    http.expectOne(`${API}/api/Student/S1/photo?v=2`).flush(new Blob(['b']));
    await settle();
    expect(el().querySelector('img')!.getAttribute('src')).toBe('blob:t/2');
  });

  it('shows the fallback when the picture fails to load, and gives a new source a fresh chance', async () => {
    fixture.componentInstance.src.set('https://cdn.example/broken.png');
    await settle();
    el().querySelector('img')!.dispatchEvent(new Event('error'));
    await settle();
    expect(el().querySelector('img')).toBeNull();
    expect(el().querySelector('.fallback')).not.toBeNull();
    fixture.componentInstance.src.set('https://cdn.example/good.png');
    await settle();
    expect(el().querySelector('img')!.getAttribute('src')).toBe('https://cdn.example/good.png');
  });
});
