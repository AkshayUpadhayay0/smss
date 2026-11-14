namespace smss_api_db_layer.repository
{
    /// <summary>Turns what a user typed into ILIKE patterns: one per word, LIKE wildcards escaped so they match literally.</summary>
    public static class SearchWords
    {
        public static IReadOnlyList<string> LikePatterns(string? search) =>
            (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => "%" + w.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%").ToList();
    }
}
