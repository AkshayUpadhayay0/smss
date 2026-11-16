// Captured from the code BEFORE the Inventory module touched route-roles.ts / nav.config.ts: what every pre-existing role saw in the sidebar and which routes it could open.
// inventory-isolation.spec.ts proves each of them is unchanged. Do not regenerate this from current code: its whole point is that it predates Inventory.
export interface GoldenRole {
  menu: { label: string; route: string | null; children: string[] | null }[];
  menuNoEmployeeRecord: string[];
  routes: string[];
}

export const PRE_INVENTORY_GOLDEN: Record<string, GoldenRole> = {
  "Super Admin": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Registered Schools",
        "route": "/schools",
        "children": null
      },
      {
        "label": "Enquiries",
        "route": "/enquiries",
        "children": null
      },
      {
        "label": "Subscriptions",
        "route": "/subscriptions",
        "children": null
      },
      {
        "label": "Invoices",
        "route": "/invoices",
        "children": null
      },
      {
        "label": "Company Finance",
        "route": null,
        "children": [
          "Overview|/company/overview",
          "Employees|/company/employees",
          "Expenses|/company/expenses"
        ]
      },
      {
        "label": "Masters",
        "route": null,
        "children": [
          "Board Type|/masters/board-type",
          "School Type|/masters/school-type",
          "School Level|/masters/school-level",
          "Status|/masters/status",
          "Role|/masters/role",
          "Religion/Caste Category|/masters/religion-category",
          "Blood Group|/masters/blood-group",
          "Gender|/masters/gender",
          "Student Category|/masters/student-category",
          "Admission Type|/masters/admission-type"
        ]
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Registered Schools",
      "Enquiries",
      "Subscriptions",
      "Invoices",
      "Company Finance",
      "Masters"
    ],
    "routes": [
      "/change-password",
      "/company/employees",
      "/company/expenses",
      "/company/overview",
      "/dashboard",
      "/dev/components-preview",
      "/enquiries",
      "/invoices",
      "/masters/admission-type",
      "/masters/blood-group",
      "/masters/board-type",
      "/masters/gender",
      "/masters/religion-category",
      "/masters/role",
      "/masters/school-level",
      "/masters/school-type",
      "/masters/status",
      "/masters/student-category",
      "/schools",
      "/schools/:schoolId/edit",
      "/schools/:schoolId/profile",
      "/schools/:schoolId/view",
      "/schools/add",
      "/subscriptions"
    ]
  },
  "School Admin": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Live Dashboard",
        "route": "/transport/live",
        "children": null
      },
      {
        "label": "School Setup",
        "route": null,
        "children": [
          "Import Masters|/school-setup/import-masters",
          "Academic Year|/school-setup/academic-year",
          "Class|/school-setup/classes",
          "Section|/school-setup/sections",
          "Periods|/school-setup/periods",
          "Subject|/school-setup/subjects",
          "Class-Subject Mapping|/school-setup/class-subjects",
          "Employee Designation|/school-setup/employee-designations",
          "Employee Department|/school-setup/employee-departments",
          "Document Types|/school-setup/document-types",
          "ID Card Settings|/school-setup/id-card"
        ]
      },
      {
        "label": "Fee Setup",
        "route": null,
        "children": [
          "Fee Types|/fee-setup/fee-types",
          "Fee Structures|/fee-setup/fee-structures",
          "Fee Concessions|/fee-setup/fee-concessions",
          "Tax Types|/fee-setup/tax-types"
        ]
      },
      {
        "label": "Examinations",
        "route": null,
        "children": [
          "Grade Scales|/examinations/grade-scales",
          "Report Schemes|/examinations/schemes",
          "Report Card Settings|/examinations/report-card-settings",
          "Activity Areas|/examinations/activity-areas",
          "Exam Schedule|/examinations/schedule",
          "Marks Entry|/examinations/marks",
          "My Class Report|/examinations/class-report",
          "Results and Publish|/examinations/results"
        ]
      },
      {
        "label": "Transport",
        "route": null,
        "children": [
          "Areas|/transport/areas",
          "Vehicles|/transport/vehicles",
          "Drivers|/transport/drivers"
        ]
      },
      {
        "label": "Employees",
        "route": null,
        "children": [
          "All Employees|/employees",
          "Add Employee|/employees/new"
        ]
      },
      {
        "label": "Fees",
        "route": null,
        "children": [
          "Fee Dashboard|/fee-dashboard",
          "Class-wise Report|/fee-reports/class",
          "Concessions given|/fee-reports/concessions-given"
        ]
      },
      {
        "label": "Students",
        "route": null,
        "children": [
          "Students|/students",
          "Admissions|/admissions",
          "Add Existing Student|/students/onboard-existing",
          "Import Students|/students/import",
          "Student Leave|/leave/students"
        ]
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "Timetable",
        "route": "/timetable",
        "children": null
      },
      {
        "label": "Mark Attendance",
        "route": "/attendance/mark",
        "children": null
      },
      {
        "label": "Attendance Report",
        "route": "/attendance/report",
        "children": null
      },
      {
        "label": "Notices",
        "route": "/notices",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      },
      {
        "label": "Homework",
        "route": "/homework",
        "children": null
      },
      {
        "label": "Assessments",
        "route": "/assessments",
        "children": null
      },
      {
        "label": "My Leave",
        "route": "/leave/my",
        "children": null
      },
      {
        "label": "Leave Requests",
        "route": "/leave/requests",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Live Dashboard",
      "School Setup",
      "Fee Setup",
      "Examinations",
      "Transport",
      "Employees",
      "Fees",
      "Students",
      "School Profile",
      "Calendar",
      "Timetable",
      "Mark Attendance",
      "Attendance Report",
      "Notices",
      "My Notices",
      "Homework",
      "Assessments",
      "Leave Requests"
    ],
    "routes": [
      "/admissions",
      "/admissions/:admissionId",
      "/admissions/:admissionId/edit",
      "/admissions/new",
      "/assessments",
      "/attendance/mark",
      "/attendance/report",
      "/calendar",
      "/change-password",
      "/dashboard",
      "/employees",
      "/employees/:employeeId",
      "/employees/:employeeId/edit",
      "/employees/new",
      "/examinations/activity-areas",
      "/examinations/class-report",
      "/examinations/grade-scales",
      "/examinations/marks",
      "/examinations/report-card-settings",
      "/examinations/results",
      "/examinations/schedule",
      "/examinations/schemes",
      "/examinations/schemes/:schemeId",
      "/examinations/schemes/blank",
      "/examinations/schemes/new",
      "/fee-dashboard",
      "/fee-reports/class",
      "/fee-reports/concessions-given",
      "/fee-setup/fee-concessions",
      "/fee-setup/fee-structures",
      "/fee-setup/fee-structures/:feeStructureId/edit",
      "/fee-setup/fee-structures/new",
      "/fee-setup/fee-types",
      "/fee-setup/tax-types",
      "/homework",
      "/leave/my",
      "/leave/requests",
      "/leave/students",
      "/notices",
      "/notices/my",
      "/school-profile",
      "/school-setup/academic-year",
      "/school-setup/class-subjects",
      "/school-setup/classes",
      "/school-setup/document-types",
      "/school-setup/employee-departments",
      "/school-setup/employee-designations",
      "/school-setup/id-card",
      "/school-setup/import-masters",
      "/school-setup/periods",
      "/school-setup/sections",
      "/school-setup/subjects",
      "/students",
      "/students/:studentId",
      "/students/import",
      "/students/onboard-existing",
      "/timetable",
      "/transport/areas",
      "/transport/drivers",
      "/transport/live",
      "/transport/vehicles"
    ]
  },
  "Principal": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Live Dashboard",
        "route": "/transport/live",
        "children": null
      },
      {
        "label": "Examinations",
        "route": null,
        "children": [
          "Grade Scales|/examinations/grade-scales",
          "Report Schemes|/examinations/schemes",
          "Report Card Settings|/examinations/report-card-settings",
          "Activity Areas|/examinations/activity-areas",
          "Exam Schedule|/examinations/schedule",
          "Marks Entry|/examinations/marks",
          "My Class Report|/examinations/class-report",
          "Results and Publish|/examinations/results"
        ]
      },
      {
        "label": "Transport",
        "route": null,
        "children": [
          "Areas|/transport/areas",
          "Vehicles|/transport/vehicles",
          "Drivers|/transport/drivers"
        ]
      },
      {
        "label": "Fees",
        "route": null,
        "children": [
          "Fee Dashboard|/fee-dashboard",
          "Class-wise Report|/fee-reports/class",
          "Concessions given|/fee-reports/concessions-given"
        ]
      },
      {
        "label": "Admissions",
        "route": "/admissions",
        "children": null
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "Attendance Report",
        "route": "/attendance/report",
        "children": null
      },
      {
        "label": "Notices",
        "route": "/notices",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      },
      {
        "label": "My Leave",
        "route": "/leave/my",
        "children": null
      },
      {
        "label": "Leave Requests",
        "route": "/leave/requests",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Live Dashboard",
      "Examinations",
      "Transport",
      "Fees",
      "Admissions",
      "School Profile",
      "Calendar",
      "Attendance Report",
      "Notices",
      "My Notices",
      "Leave Requests"
    ],
    "routes": [
      "/admissions",
      "/admissions/:admissionId",
      "/admissions/:admissionId/edit",
      "/admissions/new",
      "/attendance/report",
      "/calendar",
      "/change-password",
      "/dashboard",
      "/examinations/activity-areas",
      "/examinations/class-report",
      "/examinations/grade-scales",
      "/examinations/marks",
      "/examinations/report-card-settings",
      "/examinations/results",
      "/examinations/schedule",
      "/examinations/schemes",
      "/examinations/schemes/:schemeId",
      "/fee-dashboard",
      "/fee-reports/class",
      "/fee-reports/concessions-given",
      "/leave/my",
      "/leave/requests",
      "/my-profile",
      "/notices",
      "/notices/my",
      "/school-profile",
      "/transport/areas",
      "/transport/drivers",
      "/transport/live",
      "/transport/vehicles"
    ]
  },
  "Admission Staff": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Admissions",
        "route": "/admissions",
        "children": null
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Admissions",
      "School Profile",
      "Calendar",
      "My Notices"
    ],
    "routes": [
      "/admissions",
      "/admissions/:admissionId",
      "/admissions/:admissionId/edit",
      "/admissions/new",
      "/calendar",
      "/change-password",
      "/dashboard",
      "/my-profile",
      "/notices/my",
      "/school-profile"
    ]
  },
  "Teacher": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Examinations",
        "route": null,
        "children": [
          "Exam Schedule|/examinations/schedule",
          "Marks Entry|/examinations/marks",
          "My Class Report|/examinations/class-report",
          "Results and Publish|/examinations/results"
        ]
      },
      {
        "label": "Students",
        "route": null,
        "children": [
          "My Students|/my-students",
          "Student Leave|/leave/students",
          "Fee Status|/fee-status"
        ]
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "My Timetable",
        "route": "/timetable/my",
        "children": null
      },
      {
        "label": "Mark Attendance",
        "route": "/attendance/mark",
        "children": null
      },
      {
        "label": "Attendance Report",
        "route": "/attendance/report",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      },
      {
        "label": "Homework",
        "route": "/homework",
        "children": null
      },
      {
        "label": "Assessments",
        "route": "/assessments",
        "children": null
      },
      {
        "label": "My Leave",
        "route": "/leave/my",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Examinations",
      "Students",
      "School Profile",
      "Calendar",
      "My Timetable",
      "Mark Attendance",
      "Attendance Report",
      "My Notices",
      "Homework",
      "Assessments"
    ],
    "routes": [
      "/assessments",
      "/attendance/mark",
      "/attendance/report",
      "/calendar",
      "/change-password",
      "/dashboard",
      "/examinations/class-report",
      "/examinations/marks",
      "/examinations/results",
      "/examinations/schedule",
      "/fee-status",
      "/homework",
      "/leave/my",
      "/leave/students",
      "/my-profile",
      "/my-students",
      "/my-students/:studentId",
      "/notices/my",
      "/school-profile",
      "/timetable/my"
    ]
  },
  "Accountant": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Fees",
        "route": null,
        "children": [
          "Fee Dashboard|/fee-dashboard",
          "Class-wise Report|/fee-reports/class",
          "Concessions given|/fee-reports/concessions-given"
        ]
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      },
      {
        "label": "My Leave",
        "route": "/leave/my",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Fees",
      "School Profile",
      "Calendar",
      "My Notices"
    ],
    "routes": [
      "/calendar",
      "/change-password",
      "/dashboard",
      "/fee-dashboard",
      "/fee-reports/class",
      "/fee-reports/concessions-given",
      "/leave/my",
      "/my-profile",
      "/notices/my",
      "/school-profile",
      "/students/:studentId"
    ]
  },
  "Student": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "My Profile",
        "route": "/my-student-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      },
      {
        "label": "My Homework",
        "route": "/homework/my",
        "children": null
      },
      {
        "label": "My Results",
        "route": "/assessments/my",
        "children": null
      },
      {
        "label": "My Leave",
        "route": "/leave/mine",
        "children": null
      },
      {
        "label": "My Fees",
        "route": "/my-fees",
        "children": null
      },
      {
        "label": "My Report Cards",
        "route": "/my-report-cards",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "School Profile",
      "My Profile",
      "Calendar",
      "My Notices",
      "My Homework",
      "My Results",
      "My Leave",
      "My Fees",
      "My Report Cards"
    ],
    "routes": [
      "/assessments/my",
      "/calendar",
      "/change-password",
      "/dashboard",
      "/homework/my",
      "/leave/mine",
      "/my-fees",
      "/my-report-cards",
      "/my-student-profile",
      "/notices/my",
      "/school-profile"
    ]
  },
  "Guardian": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "School Profile",
      "Calendar",
      "My Notices"
    ],
    "routes": [
      "/calendar",
      "/change-password",
      "/dashboard",
      "/notices/my",
      "/school-profile"
    ]
  },
  "Driver": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard"
    ],
    "routes": [
      "/change-password",
      "/dashboard"
    ]
  },
  "Test Role": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard"
    ],
    "routes": [
      "/change-password",
      "/dashboard"
    ]
  },
  "(none)": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard"
    ],
    "routes": [
      "/change-password",
      "/dashboard"
    ]
  },
  "Principal+Teacher": {
    "menu": [
      {
        "label": "Dashboard",
        "route": "/dashboard",
        "children": null
      },
      {
        "label": "Live Dashboard",
        "route": "/transport/live",
        "children": null
      },
      {
        "label": "Examinations",
        "route": null,
        "children": [
          "Grade Scales|/examinations/grade-scales",
          "Report Schemes|/examinations/schemes",
          "Report Card Settings|/examinations/report-card-settings",
          "Activity Areas|/examinations/activity-areas",
          "Exam Schedule|/examinations/schedule",
          "Marks Entry|/examinations/marks",
          "My Class Report|/examinations/class-report",
          "Results and Publish|/examinations/results"
        ]
      },
      {
        "label": "Transport",
        "route": null,
        "children": [
          "Areas|/transport/areas",
          "Vehicles|/transport/vehicles",
          "Drivers|/transport/drivers"
        ]
      },
      {
        "label": "Fees",
        "route": null,
        "children": [
          "Fee Dashboard|/fee-dashboard",
          "Class-wise Report|/fee-reports/class",
          "Concessions given|/fee-reports/concessions-given"
        ]
      },
      {
        "label": "Students",
        "route": null,
        "children": [
          "My Students|/my-students",
          "Admissions|/admissions",
          "Student Leave|/leave/students",
          "Fee Status|/fee-status"
        ]
      },
      {
        "label": "School Profile",
        "route": "/school-profile",
        "children": null
      },
      {
        "label": "Calendar",
        "route": "/calendar",
        "children": null
      },
      {
        "label": "My Timetable",
        "route": "/timetable/my",
        "children": null
      },
      {
        "label": "Mark Attendance",
        "route": "/attendance/mark",
        "children": null
      },
      {
        "label": "Attendance Report",
        "route": "/attendance/report",
        "children": null
      },
      {
        "label": "Notices",
        "route": "/notices",
        "children": null
      },
      {
        "label": "My Notices",
        "route": "/notices/my",
        "children": null
      },
      {
        "label": "Homework",
        "route": "/homework",
        "children": null
      },
      {
        "label": "Assessments",
        "route": "/assessments",
        "children": null
      },
      {
        "label": "My Leave",
        "route": "/leave/my",
        "children": null
      },
      {
        "label": "Leave Requests",
        "route": "/leave/requests",
        "children": null
      }
    ],
    "menuNoEmployeeRecord": [
      "Dashboard",
      "Live Dashboard",
      "Examinations",
      "Transport",
      "Fees",
      "Students",
      "School Profile",
      "Calendar",
      "My Timetable",
      "Mark Attendance",
      "Attendance Report",
      "Notices",
      "My Notices",
      "Homework",
      "Assessments",
      "Leave Requests"
    ],
    "routes": [
      "/admissions",
      "/admissions/:admissionId",
      "/admissions/:admissionId/edit",
      "/admissions/new",
      "/assessments",
      "/attendance/mark",
      "/attendance/report",
      "/calendar",
      "/change-password",
      "/dashboard",
      "/examinations/activity-areas",
      "/examinations/class-report",
      "/examinations/grade-scales",
      "/examinations/marks",
      "/examinations/report-card-settings",
      "/examinations/results",
      "/examinations/schedule",
      "/examinations/schemes",
      "/examinations/schemes/:schemeId",
      "/fee-dashboard",
      "/fee-reports/class",
      "/fee-reports/concessions-given",
      "/fee-status",
      "/homework",
      "/leave/my",
      "/leave/requests",
      "/leave/students",
      "/my-profile",
      "/my-students",
      "/my-students/:studentId",
      "/notices",
      "/notices/my",
      "/school-profile",
      "/timetable/my",
      "/transport/areas",
      "/transport/drivers",
      "/transport/live",
      "/transport/vehicles"
    ]
  }
};
