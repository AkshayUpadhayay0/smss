import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { SecureImageService } from './secure-image.service';

const API = environment.apiUrl;
const PATH = '/api/Student/S1/photo?v=abc';

describe('SecureImageService', () => {
  let service: SecureImageService;
  let http: HttpTestingController;
  let created: string[];
  let revoked: string[];

  beforeEach(() => {
    created = []; revoked = [];
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => { const u = `blob:test/${++n}`; created.push(u); return u; });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((u) => { revoked.push(u); });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SecureImageService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); vi.restoreAllMocks(); });

  it('recognises only API paths as needing the login', () => {
    expect(SecureImageService.isSecurePath('/api/Student/S1/photo?v=1')).toBe(true);
    expect(SecureImageService.isSecurePath('/uploads/X/logo/a.png')).toBe(false);
    expect(SecureImageService.isSecurePath('https://x/y.png')).toBe(false);
    expect(SecureImageService.isSecurePath(null)).toBe(false);
  });

  it('fetches through the authenticated API client as a blob and returns an object URL', () => {
    let got: string | null = null;
    service.get(PATH).subscribe((u) => (got = u));
    const req = http.expectOne(`${API}${PATH}`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['x'], { type: 'image/jpeg' }));
    expect(got).toBe('blob:test/1');
  });

  it('caches by path: the second ask makes no request and returns the same object URL', () => {
    service.get(PATH).subscribe();
    http.expectOne(`${API}${PATH}`).flush(new Blob(['x']));
    let again: string | null = null;
    service.get(PATH).subscribe((u) => (again = u));
    http.expectNone(`${API}${PATH}`);
    expect(again).toBe('blob:test/1');
    expect(created.length).toBe(1);
  });

  it('two callers asking at the same time share one request', () => {
    const seen: (string | null)[] = [];
    service.get(PATH).subscribe((u) => seen.push(u));
    service.get(PATH).subscribe((u) => seen.push(u));
    http.expectOne(`${API}${PATH}`).flush(new Blob(['x']));
    expect(seen).toEqual(['blob:test/1', 'blob:test/1']);
  });

  it('a failure (no photo, not allowed) gives null, is not cached, and can be retried', () => {
    let got: string | null = 'unset';
    service.get(PATH).subscribe((u) => (got = u));
    http.expectOne(`${API}${PATH}`).flush(new Blob(['no']), { status: 404, statusText: 'Not Found' });
    expect(got).toBeNull();
    expect(service.size).toBe(0);
    service.get(PATH).subscribe();
    http.expectOne(`${API}${PATH}`).flush(new Blob(['x']));
    expect(service.size).toBe(1);
  });

  it('clear() revokes every object URL and forgets them (sign-out)', () => {
    service.get('/api/Student/S1/photo?v=1').subscribe(); http.expectOne(`${API}/api/Student/S1/photo?v=1`).flush(new Blob(['a']));
    service.get('/api/Student/S2/photo?v=1').subscribe(); http.expectOne(`${API}/api/Student/S2/photo?v=1`).flush(new Blob(['b']));
    service.clear();
    expect(revoked.sort()).toEqual(['blob:test/1', 'blob:test/2']);
    expect(service.size).toBe(0);
    service.get('/api/Student/S1/photo?v=1').subscribe();
    http.expectOne(`${API}/api/Student/S1/photo?v=1`).flush(new Blob(['a']));
  });

  it('is bounded: the oldest object URL is revoked when the cache is full', () => {
    for (let i = 0; i <= SecureImageService.MAX_ENTRIES; i++) {
      const p = `/api/Student/S${i}/photo?v=1`;
      service.get(p).subscribe(); http.expectOne(`${API}${p}`).flush(new Blob(['x']));
    }
    expect(service.size).toBe(SecureImageService.MAX_ENTRIES);
    expect(revoked).toEqual(['blob:test/1']);
  });

  it('never writes to browser storage', () => {
    const set = vi.spyOn(Storage.prototype, 'setItem');
    service.get(PATH).subscribe(); http.expectOne(`${API}${PATH}`).flush(new Blob(['x']));
    expect(set).not.toHaveBeenCalled();
  });
});
