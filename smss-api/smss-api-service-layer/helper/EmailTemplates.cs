using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    public static class EmailTemplates
    {
        public static (string Subject, string Html) SchoolAdminCredentials(
            string schoolName, string schoolId, string loginId, string temporaryPassword, string? loginUrl)
        {
            var subject = $"Your SMSS login for {schoolName}";
            var loginLine = string.IsNullOrEmpty(loginUrl)
                ? ""
                : $"<p>Login here: <a href=\"{loginUrl}\">{loginUrl}</a></p>";

            var html = $@"
                <p>Hello,</p>
                <p><strong>{schoolName}</strong> (School ID: {schoolId}) has been registered on SMSS.</p>
                <p>Your login details:</p>
                <ul>
                    <li><strong>Login ID:</strong> {loginId}</li>
                    <li><strong>Temporary Password:</strong> {temporaryPassword}</li>
                </ul>
                <p>You will be asked to set a new password the first time you log in. Please do not share this email.</p>
                {loginLine}";

            return (subject, html);
        }

        private static string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        public static (string Subject, string Html) ApplicationSubmitted(string applicantName, string applicationNumber, string schoolName)
        {
            var subject = $"Application {applicationNumber} received - {schoolName}";
            var html = $@"
                <p>Hello,</p>
                <p>The admission application for <strong>{Enc(applicantName)}</strong> has been received by <strong>{Enc(schoolName)}</strong>.</p>
                <p><strong>Application number:</strong> {Enc(applicationNumber)}</p>
                <p>The school will review the application and get back to you. Please keep this number for reference.</p>";
            return (subject, html);
        }

        public static (string Subject, string Html) ApplicationApproved(string applicantName, string applicationNumber, string schoolName)
        {
            var subject = $"Application {applicationNumber} approved - {schoolName}";
            var html = $@"
                <p>Hello,</p>
                <p>Good news: the admission application for <strong>{Enc(applicantName)}</strong> to <strong>{Enc(schoolName)}</strong> has been approved.</p>
                <p><strong>Application number:</strong> {Enc(applicationNumber)}</p>
                <p>The school will contact you about the next steps to complete the admission.</p>";
            return (subject, html);
        }

        public static (string Subject, string Html) StudentIdAssigned(string studentName, string studentId, string schoolName)
        {
            var subject = $"Student ID assigned - {schoolName}";
            var html = $@"
                <p>Hello,</p>
                <p><strong>{Enc(studentName)}</strong> is now a student of <strong>{Enc(schoolName)}</strong>.</p>
                <p><strong>Student ID:</strong> {Enc(studentId)}</p>
                <p>Please keep this ID: it is also the Login ID once a student login is created.</p>";
            return (subject, html);
        }

        public static (string Subject, string Html) StudentCredentials(
            string studentName, string schoolName, string loginId, string temporaryPassword, string? loginUrl)
        {
            var subject = $"Your SMSS student login for {schoolName}";
            var loginLine = string.IsNullOrEmpty(loginUrl)
                ? ""
                : $"<p>Login here: <a href=\"{loginUrl}\">{loginUrl}</a></p>";

            var html = $@"
                <p>Hello,</p>
                <p>A student login has been created for <strong>{studentName}</strong> at <strong>{schoolName}</strong> on SMSS.</p>
                <p>Login details:</p>
                <ul>
                    <li><strong>Login ID:</strong> {loginId}</li>
                    <li><strong>Temporary Password:</strong> {temporaryPassword}</li>
                </ul>
                <p>The password must be changed the first time you log in. Please do not share this email.</p>
                {loginLine}";

            return (subject, html);
        }

        public static (string Subject, string Html) EmployeeCredentials(
            string employeeName, string schoolName, string loginId, string temporaryPassword, string? loginUrl)
        {
            var subject = $"Your SMSS login for {schoolName}";
            var loginLine = string.IsNullOrEmpty(loginUrl)
                ? ""
                : $"<p>Login here: <a href=\"{loginUrl}\">{loginUrl}</a></p>";

            var html = $@"
                <p>Hello {employeeName},</p>
                <p>A login has been created for you at <strong>{schoolName}</strong> on SMSS.</p>
                <p>Login details:</p>
                <ul>
                    <li><strong>Login ID:</strong> {loginId}</li>
                    <li><strong>Temporary Password:</strong> {temporaryPassword}</li>
                </ul>
                <p>The password must be changed the first time you log in. Please do not share this email.</p>
                {loginLine}";

            return (subject, html);
        }

        public static (string Subject, string Html) SuperAdminWelcome(string displayName, string orgUserId, string? loginUrl)
        {
            var subject = "Your SMSS Super Admin account is ready";
            var loginLine = string.IsNullOrEmpty(loginUrl)
                ? ""
                : $"<p>Login here: <a href=\"{loginUrl}\">{loginUrl}</a></p>";

            var html = $@"
                <p>Hello {displayName},</p>
                <p>Your Super Admin account has been created on SMSS.</p>
                <p><strong>Login ID:</strong> {orgUserId}</p>
                <p>Use the password you set during setup to log in.</p>
                {loginLine}";

            return (subject, html);
        }
    }
}
