using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    internal class Constants
    {
    }
    public static class UserTypes
    {
        public const string SchoolId = "School Id";
        public const string SuperAdmin = "Super Admin";
        public const string Student = "Student";
        public const string Employee = "Employee";
    }

    public static class RoleCodes
    {
        /// <summary>Never handed out by a school: only the Super Admin bootstrap creates this role's users.</summary>
        public const string SuperAdmin = "SUPER_ADMIN";
        public const string SchoolAdmin = "SCHOOL_ADMIN";
        public const string Principal = "PRINCIPAL";
        public const string Teacher = "TEACHER";
        public const string Accountant = "ACCOUNTANT";
        public const string Student = "STUDENT";
        public const string Guardian = "GUARDIAN";

        /// <summary>The roles a school may give an EMPLOYEE's login, by role_code (an allow-list: add e.g. LIBRARIAN here once that role exists in lut_roles).</summary>
        private static readonly string[] EmployeeAllowList = { SchoolAdmin, Principal, Teacher, Accountant };
        /// <summary>Never given to an employee login, even if someone adds them to the allow-list by mistake.</summary>
        private static readonly string[] NeverForEmployees = { SuperAdmin, Student, Guardian };

        public static IReadOnlyCollection<string> AssignableToEmployees { get; } = EmployeeAllowList.Except(NeverForEmployees).ToArray();
    }

    public static class EmploymentStatusCodes
    {
        public const string Active = "ACTIVE";
    }

    public static class EmployeeTypes
    {
        public const string Teaching = "Teaching";
        public const string NonTeaching = "Non-Teaching";
    }

    public static class RoleNames
    {
        public const string Accountant = "Accountant";
        public const string Principal = "Principal";
        public const string Teacher = "Teacher";
        public const string SchoolAdmin = "School Admin";
        public const string SuperAdmin = "Super Admin";
        public const string Student = "Student";
    }
    public static class PaymentStatuses
    {
        public const string Active = "Active";
        public const string Voided = "Voided";
    }
    public static class TenantMessages
    {
        public const string NoSchool = "This account is not linked to a school.";
    }
    public static class StatusNames
    {
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string GeneralType = "general status";   // matches lut_status.stype
    }
}
