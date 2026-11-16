import { visibleNav } from './nav.config';

describe('nav: Fee Setup', () => {
  const feeSetup = (roles: string[]) => visibleNav(roles).find((i) => i.label === 'Fee Setup');

  it('lists Fee Types, Fee Structures, Fee Concessions, then Tax Types for a School Admin', () => {
    expect(feeSetup(['School Admin'])?.children?.map((c) => c.label)).toEqual(['Fee Types', 'Fee Structures', 'Fee Concessions', 'Tax Types']);
  });

  it('Tax Types points at /fee-setup/tax-types', () => {
    expect(feeSetup(['School Admin'])?.children?.find((c) => c.label === 'Tax Types')?.route).toBe('/fee-setup/tax-types');
  });

  it('nobody else sees Fee Setup or Tax Types (School Admin only)', () => {
    for (const role of ['Accountant', 'Principal', 'Teacher', 'Student', 'Guardian', 'Admission Staff', 'Super Admin']) {
      expect(feeSetup([role])).toBeUndefined();
    }
  });

});

describe('nav: Fees', () => {
  const fees = (roles: string[]) => visibleNav(roles).find((i) => i.label === 'Fees');

  it('lists Fee Dashboard, Class-wise Report, then Concessions given for School Admin, Accountant and Principal', () => {
    for (const role of ['School Admin', 'Accountant', 'Principal']) {
      expect(fees([role])?.children?.map((c) => c.label)).toEqual(['Fee Dashboard', 'Class-wise Report', 'Concessions given']);
      expect(fees([role])?.children?.find((c) => c.label === 'Concessions given')?.route).toBe('/fee-reports/concessions-given');
    }
  });

  it('nobody else sees Concessions given', () => {
    for (const role of ['Teacher', 'Student', 'Guardian', 'Admission Staff', 'Super Admin']) {
      expect(fees([role])?.children?.some((c) => c.label === 'Concessions given') ?? false).toBe(false);
    }
  });
});
