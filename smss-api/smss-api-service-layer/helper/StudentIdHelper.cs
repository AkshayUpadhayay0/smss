namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Student id = {school short code}ST{nnn}, e.g. DPS001ST001. It is the primary key of tb_students, is generated once when
    /// the student record is created (confirm admission / direct entry), and is reused unchanged as the student's login id.
    /// </summary>
    public static class StudentIdHelper
    {
        public const string NoShortCodeMessage = "This school has no short code configured — contact support.";

        /// <param name="sequence">How many students the school already has, plus one.</param>
        public static string Build(string schoolShortCode, int sequence) => $"{schoolShortCode}ST{sequence:D3}";
    }
}
