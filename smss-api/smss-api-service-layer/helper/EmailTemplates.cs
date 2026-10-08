using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    public static class EmailTemplates
    {
        public static (string Subject, string Html) SchoolAdminCredentials(
            string schoolName, string schoolId, string username, string temporaryPassword, string? loginUrl)
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
                    <li><strong>Username:</strong> {username}</li>
                    <li><strong>Temporary Password:</strong> {temporaryPassword}</li>
                </ul>
                <p>You will be asked to set a new password the first time you log in. Please do not share this email.</p>
                {loginLine}";

            return (subject, html);
        }

        public static (string Subject, string Html) SuperAdminWelcome(string username, string orgUserId, string? loginUrl)
        {
            var subject = "Your SMSS Super Admin account is ready";
            var loginLine = string.IsNullOrEmpty(loginUrl)
                ? ""
                : $"<p>Login here: <a href=\"{loginUrl}\">{loginUrl}</a></p>";

            var html = $@"
                <p>Hello {username},</p>
                <p>Your Super Admin account has been created on SMSS.</p>
                <p><strong>Username:</strong> {username}<br/><strong>Admin ID:</strong> {orgUserId}</p>
                <p>Use the password you set during setup to log in.</p>
                {loginLine}";

            return (subject, html);
        }
    }
}
