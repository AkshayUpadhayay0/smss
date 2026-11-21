namespace smss_api_service_layer.helper
{
    // The one message for "this academic year is closed" (HTTP 409). Closed years refuse configuration, attendance,
    // enrolment, homework, class-test and timetable writes; fee payments, voids, receipts and reads are never blocked.
    public static class ClosedYear
    {
        public const string Message = "This academic year is closed. A School Admin can reopen it from Academic Years if a change is really needed.";
    }
}
