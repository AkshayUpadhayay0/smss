namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Employee id = {school short code}EM{nnn}, e.g. DPS001EM001. The primary key of tb_employees, generated once when the employee is created
    /// and reused unchanged as the employee's login id. (Same idea as StudentIdHelper's ...ST...)
    /// </summary>
    public static class EmployeeIdHelper
    {
        /// <param name="sequence">The highest number already used in the school, plus one.</param>
        public static string Build(string schoolShortCode, int sequence) => $"{schoolShortCode}EM{sequence:D3}";
        public static string Prefix(string schoolShortCode) => $"{schoolShortCode}EM";
    }
}
