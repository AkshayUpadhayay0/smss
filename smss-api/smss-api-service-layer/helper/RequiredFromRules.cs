namespace smss_api_service_layer.helper
{
    /// <summary>
    /// A document type has a "required from" date exactly while it is required (the database check says so). It is set to TODAY when the type is switched on, kept while it stays on
    /// (so editing a name does not move it), and cleared when it is switched off. The requirement applies to students admitted on or after that date.
    /// </summary>
    public static class RequiredFromRules
    {
        public static DateTime? Resolve(bool isRequired, DateTime? current, DateOnly today) =>
            !isRequired ? null : current ?? today.ToDateTime(TimeOnly.MinValue);
    }
}
