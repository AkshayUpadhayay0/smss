import { employmentStatusTone, formatFileSize } from './employee.model';

describe('employee model helpers', () => {
  it('colours each employment status', () => {
    expect(['Active', 'On Leave', 'Resigned', 'Terminated', 'Retired'].map(employmentStatusTone)).toEqual(['green', 'yellow', 'gray', 'red', 'blue']);
  });

  it('formats file sizes', () => {
    expect([500, 2048, 5 * 1024 * 1024].map(formatFileSize)).toEqual(['500 B', '2.0 KB', '5.0 MB']);
  });
});
