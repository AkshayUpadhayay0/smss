namespace smss_api_service_layer.helper
{
    /// <summary>The school's latitude and longitude: both or neither, each inside its range, at most six decimals (the column is numeric(9,6)).</summary>
    public static class SchoolLocation
    {
        public static string? Error(decimal? latitude, decimal? longitude)
        {
            if (latitude == null && longitude == null) return null;
            if (latitude == null || longitude == null) return "Latitude and longitude must be given together, or both left empty.";
            if (latitude < -90m || latitude > 90m) return "Latitude must be between -90 and 90.";
            if (longitude < -180m || longitude > 180m) return "Longitude must be between -180 and 180.";
            if (decimal.Round(latitude.Value, 6) != latitude || decimal.Round(longitude.Value, 6) != longitude) return "Latitude and longitude can have at most 6 decimal places.";
            return null;
        }
    }
}
