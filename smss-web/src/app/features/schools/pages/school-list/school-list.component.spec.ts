import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { API, envelope, flushLookups, makeSchool, setupHttp } from '../../testing/school-test-utils';
import { SchoolListComponent } from './school-list.component';

describe('SchoolListComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<SchoolListComponent>;
  let confirm: ConfirmDialogService;

  const el = () => fixture.nativeElement as HTMLElement;
  const bodyRows = () => [...el().querySelectorAll('tbody tr')];
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label));

  beforeEach(() => {
    http = setupHttp();
    confirm = TestBed.inject(ConfirmDialogService);
    fixture = TestBed.createComponent(SchoolListComponent);
    fixture.detectChanges();

    flushLookups(http); // general-status lookup
    http.expectOne(`${API}/api/SchoolRegistration/schools`).flush(
      envelope([
        makeSchool({ schoolId: 's1', schoolCode: 'AAA-1', schoolName: 'Alpha School', email: 'alpha@x.com', schoolStatusId: 1 }),
        makeSchool({ schoolId: 's2', schoolCode: 'BBB-2', schoolName: 'Beta School', email: null, mobileNumber: '9000000001', schoolStatusId: 2 }),
        makeSchool({ schoolId: 's3', schoolCode: 'CCC-3', schoolName: 'Gamma Academy', email: 'gamma@x.com', schoolStatusId: 1 }),
      ]),
    );
    fixture.detectChanges();
  });
  afterEach(() => http.verify());

  it('lists schools with the status resolved from schoolStatusId (never the raw number)', () => {
    expect(bodyRows()).toHaveLength(3);
    const pills = [...el().querySelectorAll('.pill')].map((p) => p.textContent?.trim());
    expect(pills).toEqual(['Active', 'Inactive', 'Active']);
    expect(el().textContent).toContain('3 records');
  });

  it('filters client-side across visible columns', () => {
    const input = el().querySelector<HTMLInputElement>('input[type="text"]')!;
    input.value = 'gamma';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(bodyRows()).toHaveLength(1);
    expect(bodyRows()[0].textContent).toContain('Gamma Academy');

    input.value = 'inactive'; // matches the Status column
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(bodyRows()).toHaveLength(1);
    expect(bodyRows()[0].textContent).toContain('Beta School');

    input.value = 'zzz';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(el().textContent).toContain('No schools match your search.');
  });

  it('sorts when a sortable header is clicked', () => {
    const header = [...el().querySelectorAll('th button.sort')].find((b) => b.textContent?.includes('School Name')) as HTMLButtonElement;
    header.click();
    fixture.detectChanges();
    expect(bodyRows()[0].textContent).toContain('Alpha School');
    header.click();
    fixture.detectChanges();
    expect(bodyRows()[0].textContent).toContain('Gamma Academy');
  });

  it('shows Deactivate for active rows and Activate for inactive rows', () => {
    const labels = bodyRows().map((r) => (r.textContent?.includes('Deactivate') ? 'Deactivate' : r.textContent?.includes('Activate') ? 'Activate' : ''));
    expect(labels).toEqual(['Deactivate', 'Activate', 'Deactivate']);
  });

  it('asks for confirmation, then toggles status and updates the row', async () => {
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    (bodyRows()[0].querySelectorAll('button')[2] as HTMLButtonElement).click();
    await fixture.whenStable();

    const req = http.expectOne(`${API}/api/SchoolRegistration/schools/s1/toggle-status`);
    expect(req.request.method).toBe('POST');
    req.flush(envelope(makeSchool({ schoolId: 's1', schoolCode: 'AAA-1', schoolName: 'Alpha School', schoolStatusId: 2 })));
    fixture.detectChanges();

    expect(bodyRows()[0].querySelector('.pill')?.textContent?.trim()).toBe('Inactive');
    expect(bodyRows()[0].textContent).toContain('Activate');
  });

  it('does nothing when the confirmation is cancelled', async () => {
    vi.spyOn(confirm, 'confirm').mockResolvedValue(false);
    (bodyRows()[0].querySelectorAll('button')[2] as HTMLButtonElement).click();
    await fixture.whenStable();
    http.expectNone((r) => r.url.includes('toggle-status'));
    expect(button('Deactivate')).toBeTruthy();
  });
});
