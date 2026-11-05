import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ToastService } from '../../../../core/services/toast.service';
import { StudentMyIdCardComponent } from './student-my-id-card.component';

const API = environment.apiUrl;
const PREVIEW = `${API}/api/Student/me/id-card/preview`;
const PDF = `${API}/api/Student/me/id-card`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const PNG = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
const CALM = 'Your ID card is not available right now. Please ask the school office.';

describe('StudentMyIdCardComponent', () => {
  let fixture: ComponentFixture<StudentMyIdCardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  function open(respond: (r: ReturnType<HttpTestingController['expectOne']>) => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentMyIdCardComponent);
    fixture.detectChanges();
    respond(http.expectOne(PREVIEW));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the front first, then the back with the switch, as the school\'s own image', () => {
    open((r) => r.flush(env({ templateCode: 'modern-navy', frontPng: PNG, backPng: PNG + 'AA', isSample: false })));
    const img = () => el().querySelector('img') as HTMLImageElement;
    expect(img().alt).toBe('Front of your ID card');
    expect(img().src).toBe(`data:image/png;base64,${PNG}`);
    ([...el().querySelectorAll('.switch button')][1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(img().alt).toBe('Back of your ID card');
    expect(img().src).toBe(`data:image/png;base64,${PNG}AA`);
    expect([...el().querySelectorAll('.switch button')].map((b) => b.getAttribute('aria-pressed'))).toEqual(['false', 'true']);
  });

  it('a card that cannot be issued shows the server\'s calm message in place: no image, no download, no error toast', () => {
    open((r) => r.flush({ status: false, statusCode: 404, message: CALM, data: null }, { status: 404, statusText: 'Not Found' }));
    expect(el().textContent).toContain(CALM);
    expect(el().querySelector('img')).toBeNull();
    expect(el().querySelector('button')).toBeNull();
    expect(TestBed.inject(ToastService).toasts().length).toBe(0);
  });

  it('Download asks for the PDF and saves it', () => {
    open((r) => r.flush(env({ templateCode: 'classic-blue', frontPng: PNG, backPng: PNG, isSample: false })));
    const saved: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { saved.push(this.download); });
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    ([...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Download')) as HTMLButtonElement).click();
    const r = http.expectOne(PDF);
    expect(r.request.method).toBe('GET');
    r.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    expect(saved).toEqual(['IDCard.pdf']);
  });

  it('a failed download toasts the server message once', async () => {
    open((r) => r.flush(env({ templateCode: 'classic-blue', frontPng: PNG, backPng: PNG, isSample: false })));
    ([...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Download')) as HTMLButtonElement).click();
    http.expectOne(PDF).flush(new Blob([JSON.stringify({ message: CALM })], { type: 'application/json' }), { status: 404, statusText: 'Not Found' });
    await new Promise((res) => setTimeout(res, 10));
    expect(TestBed.inject(ToastService).toasts().some((t) => String(t.message ?? t.title).includes('not available'))).toBe(true);
  });
});
