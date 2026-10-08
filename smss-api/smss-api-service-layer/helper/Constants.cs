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
    }

    public static class RoleNames
    {
        public const string SchoolAdmin = "School Admin";
        public const string SuperAdmin = "Super Admin";
    }
    public static class StatusNames
    {
        public const string Active = "Active";
        public const string Inactive = "Inactive";
        public const string GeneralType = "general status";   // matches lut_status.stype
    }
}
