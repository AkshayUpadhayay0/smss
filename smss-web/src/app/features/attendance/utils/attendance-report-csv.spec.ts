import { MonthlyAttendanceReport } from '../models/attendance-report.model';
import { csvField, reportFileName, reportToCsv } from './attendance-report-csv';

const days = ['2026-11-01', '2026-11-02', '2026-11-03', '2026-11-06'].map((date, i) => ({
  date, dayOfWeek: i === 0 ? 7 : i + 0, isWorkingDay: i !== 0, nonWorkingReason: i === 0 ? 'Sunday' : null, holidayName: null, isFuture: i === 3, markedCount: 1,
}));
const R: MonthlyAttendanceReport = {
  sectionId: 1, classId: 1, className: 'Class 3', sectionName: 'A', academicYearId: 1, academicYearName: '2026-27', year: 2026, month: 11, monthName: 'November 2026', today: '2026-11-05', days,
  students: [
    { studentId: 'a', fullName: 'Rana, Pari "Pip"', rollNumber: '1', isActive: true, marks: ['NonWorking', 'Present', 'Absent', 'NotMarked'], presentDays: 1, absentDays: 1, notMarkedDays: 0, percentage: 50, belowThreshold: true },
    { studentId: 'b', fullName: '=cmd|calc', rollNumber: null, isActive: false, marks: ['NotEnrolled', 'NotMarked', 'Present', 'NotMarked'], presentDays: 1, absentDays: 0, notMarkedDays: 1, percentage: 100, belowThreshold: false },
  ],
  summary: { studentCount: 2, averagePercentage: 75, studentsBelowThreshold: 1, thresholdPercentage: 75, workingDays: 2, daysNotMarked: 0, notMarkedDates: [] },
};

describe('attendance report CSV', () => {
  it('has a header with the day numbers and one row per student', () => {
    const lines = reportToCsv(R).trimEnd().split('\r\n');
    expect(lines[0]).toBe('Roll,Student,1,2,3,6,Present,Absent,Attendance %');
    expect(lines).toHaveLength(3);
  });

  it('writes P / A / - and leaves non-counted and future days blank', () => {
    const lines = reportToCsv(R).trimEnd().split('\r\n');
    expect(lines[1]).toBe('1,"Rana, Pari ""Pip""",,P,A,,1,1,50');
    expect(lines[2]).toBe(",'=cmd|calc (left),,-,P,,1,0,100");
  });

  it('quotes commas, quotes and line breaks; neutralises a leading formula character', () => {
    expect(csvField('a,b')).toBe('"a,b"');
    expect(csvField('say "hi"')).toBe('"say ""hi"""');
    expect(csvField('line\nbreak')).toBe('"line\nbreak"');
    expect(csvField('=1+1')).toBe("'=1+1");
    expect(csvField('+91')).toBe("'+91");
    expect(csvField('-')).toBe('-');
    expect(csvField(null)).toBe('');
    expect(csvField(33.3)).toBe('33.3');
  });

  it('names the file after class, section and month', () => {
    expect(reportFileName(R, 'csv')).toBe('attendance-Class-3-A-2026-11.csv');
    expect(reportFileName({ ...R, month: 3 }, 'pdf')).toBe('attendance-Class-3-A-2026-03.pdf');
  });
});
