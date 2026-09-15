import { UserTableRow } from '../models';

const FIRST_NAMES = ['Aarav', 'Diya', 'Kabir', 'Isha', 'Vihaan', 'Anaya', 'Reyansh', 'Myra', 'Arjun', 'Saanvi', 'Sai', 'Aadhya', 'Vivaan', 'Ananya', 'Aryan', 'Ira', 'Dhruv', 'Kiara', 'Yash', 'Riya'];
const LAST_NAMES = ['Sharma', 'Verma', 'Nair', 'Kumar', 'Gupta', 'Iyer', 'Reddy', 'Singh', 'Patel', 'Rao', 'Mehta', 'Joshi', 'Chopra', 'Das', 'Bose'];
const ORGS = ['Demo School', 'ABC International School', 'Northgate Academy', 'Sunrise Public School'];
const ROLES = ['Administrator', 'Manager', 'Staff', 'Viewer'];
const STATUSES: UserTableRow['status'][] = ['Active', 'Inactive', 'Pending'];

function seededRandom(seed: number) {
  let value = seed;
  return () => {
    value = (value * 9301 + 49297) % 233280;
    return value / 233280;
  };
}

function buildRows(count: number): UserTableRow[] {
  const rand = seededRandom(42);
  const rows: UserTableRow[] = [];

  for (let i = 1; i <= count; i++) {
    const first = FIRST_NAMES[Math.floor(rand() * FIRST_NAMES.length)];
    const last = LAST_NAMES[Math.floor(rand() * LAST_NAMES.length)];
    const org = ORGS[Math.floor(rand() * ORGS.length)];
    const role = ROLES[Math.floor(rand() * ROLES.length)];
    const status = STATUSES[Math.floor(rand() * STATUSES.length)];
    const day = 1 + Math.floor(rand() * 27);
    const month = 1 + Math.floor(rand() * 12);

    rows.push({
      id: `USR-${String(1000 + i)}`,
      name: `${first} ${last}`,
      email: `${first.toLowerCase()}.${last.toLowerCase()}@example.com`,
      organization: org,
      role,
      status,
      createdDate: `2026-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`,
    });
  }

  return rows;
}

export const TABLE_MOCK_ROWS: UserTableRow[] = buildRows(64);
