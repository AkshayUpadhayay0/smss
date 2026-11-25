import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { PendingPromotionTileComponent } from './pending-promotion-tile.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const PENDING = (over: Record<string, unknown> = {}) => ({ hasPair: true, sourceYearName: '2026-27', targetYearName: '2027-28', sourceYearId: 2, targetYearId: 3, targetIsCurrent: false, total: 12, blocked: 3, rows: [], ...over });

describe('PendingPromotionTileComponent', () => {
  let fixture: ComponentFixture<PendingPromotionTileComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  function open(answer: (req: import('@angular/common/http/testing').TestRequest) => void) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PendingPromotionTileComponent);
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${API}/api/Promotion/pending`);
    expect(req.request.params.get('summary')).toBe('true');
    answer(req);
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('shows the count and links to the pending list', () => {
    open((r) => r.flush(env(PENDING())));
    expect(root().querySelector('[data-pending-count]')?.textContent).toBe('12');
    expect(root().querySelector('a[data-pending-tile]')?.getAttribute('href')).toBe('/promotion/pending');
    expect(root().querySelector('[data-pair]')?.textContent).toContain('from 2026-27 not yet in 2027-28');   // it names the pair
  });

  it('shows a real 0 as 0 when the school has an earlier year and nobody is waiting', () => {
    open((r) => r.flush(env(PENDING({ total: 0 }))));
    expect(root().querySelector('[data-pending-count]')?.textContent).toBe('0');
  });

  it('is hidden when the school has no pair', () => {
    open((r) => r.flush(env(PENDING({ hasPair: false, total: 0 }))));
    expect(root().querySelector('[data-pending-tile]')).toBeNull();
  });

  it('says Unavailable, never 0, when the count cannot be read', () => {
    open((r) => r.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(root().querySelector('[data-pending-count]')?.textContent).toBe('Unavailable');
  });
});
