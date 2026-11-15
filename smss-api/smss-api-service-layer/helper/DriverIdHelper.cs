namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Driver id = {school short code}DR{nnn}, e.g. DPS001DR001. The primary key of tb_drivers, generated once when the driver is created
    /// and reused unchanged as the driver's login id. (Same idea as EmployeeIdHelper's ...EM...)
    /// </summary>
    public static class DriverIdHelper
    {
        /// <param name="sequence">The highest number already used in the school, plus one.</param>
        public static string Build(string schoolShortCode, int sequence) => $"{schoolShortCode}DR{sequence:D3}";
        public static string Prefix(string schoolShortCode) => $"{schoolShortCode}DR";
    }
}
