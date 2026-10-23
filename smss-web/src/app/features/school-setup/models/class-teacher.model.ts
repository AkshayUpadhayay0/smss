// Mirrors ClassTeacherAssignmentDto / ClassTeacherListDto / EligibleTeacherDto (class teacher of each section, per academic year).

export interface ClassTeacherSection {
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  assignmentId?: number | null;
  employeeId?: string | null;
  employeeName?: string | null;
  hasClassTeacher: boolean;
}

export interface ClassTeacherList {
  academicYearId: number;
  academicYearName: string;
  sections: ClassTeacherSection[];
  unassignedCount: number;
}

export interface ClassTeacherAssignment {
  assignmentId: number;
  academicYearId: number;
  classId: number;
  className: string;
  sectionId: number;
  sectionName: string;
  employeeId: string;
  employeeName: string;
  statusName: string;
  assignedAt: string;
  updatedAt: string;
}

export interface EligibleTeacher {
  employeeId: string;
  fullName: string;
  designationName: string;
}
