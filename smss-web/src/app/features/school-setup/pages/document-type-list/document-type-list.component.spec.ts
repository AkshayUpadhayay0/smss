import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { DocumentTypeListComponent } from './document-type-list.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const TYPE = (id: number, over: Record<string, unknown> = {}) => ({ documentTypeId: id, schoolId: 'S1', documentName: 'Doc ' + id, documentCode: 'D' + id, appliesTo: 'STUDENT', isRequired: false, requiredFrom: null, isActive: true, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', ...over });

describe('DocumentTypeListComponent: Required from', () => {
  let fixture: ComponentFixture<DocumentTypeListComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S1', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DocumentTypeListComponent);
    fixture.detectChanges();
  });
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the day a required type started to apply, and a dash while it is not required', async () => {
    http.expectOne((r) => r.url.startsWith(`${API}/api/DocumentType`)).flush(env([TYPE(1, { isRequired: true, requiredFrom: '2026-11-20T00:00:00' }), TYPE(2)]));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const headers = [...el.querySelectorAll('th')].map((h) => h.textContent?.trim());
    expect(headers).toContain('Required from');
    const rows = [...el.querySelectorAll('tbody tr')].map((r) => r.textContent ?? '');
    expect(rows.find((r) => r.includes('Doc 1'))).toContain('20 Nov 2026');
    expect(rows.find((r) => r.includes('Doc 2'))).not.toContain('2026');
  });
});
