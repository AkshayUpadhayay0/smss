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

        /// <summary>Driver login credentials. Names are HTML-encoded (they come from a form).</summary>
        public static (string Subject, string Html) DriverCredentials(
            string driverName, string schoolName, string loginId, string temporaryPassword, string? loginUrl)
        {
            var name = System.Net.WebUtility.HtmlEncode(driverName);
            var school = System.Net.WebUtility.HtmlEncode(schoolName);
            var subject = $"Your SMSS login for {schoolName}";
            var loginLine = string.IsNullOrEmpty(loginUrl) ? "" : $"<p>Login here: <a href=\"{System.Net.WebUtility.HtmlEncode(loginUrl)}\">{System.Net.WebUtility.HtmlEncode(loginUrl)}</a></p>";

            var html = $@"
                <p>Hello {name},</p>
                <p>A driver login has been created for you at <strong>{school}</strong> on SMSS.</p>
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

        // ------------------------------------------------------------------------------------------------ parent accounts (stage 6): a one-time LINK, never a password
        private static string LinkButton(string link, string text) => $"<p><a href=\"{Enc(link)}\">{Enc(text)}</a></p>";

        public static (string Subject, string Html) ParentAccountReady(string guardianName, string childName, string schoolName, string loginId, string link, int linkHours)
        {
            var subject = $"Your parent login for {schoolName}";
            var html = $@"
                <p>Hello {Enc(guardianName)},</p>
                <p><strong>{Enc(childName)}</strong> has been admitted to <strong>{Enc(schoolName)}</strong>. A parent login has been created for you.</p>
                <p><strong>Your login ID:</strong> {Enc(loginId)}</p>
                <p>Use the link below to choose your own password. The link works once and expires in {linkHours} hours.</p>
                {LinkButton(link, "Set your password")}
                <p>If you did not expect this message, you can ignore it.</p>";
            return (subject, html);
        }

        public static (string Subject, string Html) ParentChildAdded(string guardianName, string childName, string schoolName, string loginId, string? loginUrl)
        {
            var subject = $"{childName} was added to your parent login";
            var html = $@"
                <p>Hello {Enc(guardianName)},</p>
                <p><strong>{Enc(childName)}</strong> of <strong>{Enc(schoolName)}</strong> has been added to your parent login. Sign in to see all your children.</p>
                <p><strong>Your login ID:</strong> {Enc(loginId)}</p>
                {(string.IsNullOrEmpty(loginUrl) ? "" : LinkButton(loginUrl, "Open the login page"))}";
            return (subject, html);
        }

        public static (string Subject, string Html) ParentLoginHelp(string guardianName, string schoolName, string loginId, string link, int linkMinutes)
        {
            var subject = "Your parent login details";
            var html = $@"
                <p>Hello {Enc(guardianName)},</p>
                <p>We received a request to recover your parent login for <strong>{Enc(schoolName)}</strong>.</p>
                <p><strong>Your login ID:</strong> {Enc(loginId)}</p>
                <p>Use the link below to choose a new password. The link works once and expires in {linkMinutes} minutes.</p>
                {LinkButton(link, "Choose a new password")}
                <p>If you did not ask for this, you can ignore it: your password has not changed.</p>";
            return (subject, html);
        }

        public static (string Subject, string Html) ParentStudentLoginReady(string guardianName, string studentName, string schoolName, string studentLoginId, string link, int linkHours)
        {
            var subject = $"Student login for {studentName} - {schoolName}";
            var html = $@"
                <p>Hello {Enc(guardianName)},</p>
                <p>A student login has been created for <strong>{Enc(studentName)}</strong> at <strong>{Enc(schoolName)}</strong>.</p>
                <p><strong>The student's login ID:</strong> {Enc(studentLoginId)}</p>
                <p>Use the link below to set the student's password. The link works once and expires in {linkHours} hours.</p>
                {LinkButton(link, "Set the student's password")}";
            return (subject, html);
        }
    }
}
