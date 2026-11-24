import { createSchoolForm, locationValidator, rangeValidator, toUpdateRequest } from './school-form';
import { FormControl } from '@angular/forms';

describe('school WhatsApp and location', () => {
  it('latitude and longitude go together', () => {
    const f = createSchoolForm();
    f.controls.latitude.setValue('26.8467');
    expect(locationValidator(f)).toEqual({ location: true });
    f.controls.longitude.setValue('80.9462');
    expect(locationValidator(f)).toBeNull();
    f.controls.latitude.setValue('');
    f.controls.longitude.setValue('');
    expect(locationValidator(f)).toBeNull();
  });
  it('rejects out-of-range values', () => {
    expect(rangeValidator(-90, 90, 'Latitude')(new FormControl('95'))).not.toBeNull();
    expect(rangeValidator(-180, 180, 'Longitude')(new FormControl('-181'))).not.toBeNull();
    expect(rangeValidator(-90, 90, 'Latitude')(new FormControl('12.5'))).toBeNull();
  });
  it('the Super Admin request carries WhatsApp and location; the same-as-mobile tick sends no number', () => {
    const f = createSchoolForm();
    f.patchValue({ mobileNumber: '9876543210', whatsAppSameAsMobile: true, latitude: '26.8467', longitude: '80.9462' });
    const r = toUpdateRequest(f);
    expect(r.whatsAppNumber).toBeNull();
    expect(r.whatsAppSameAsMobile).toBe(true);
    expect(r.latitude).toBe(26.8467);
    expect(r.longitude).toBe(80.9462);
  });
});
