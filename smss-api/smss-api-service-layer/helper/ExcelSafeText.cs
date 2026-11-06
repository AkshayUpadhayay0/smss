using ClosedXML.Excel;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Writes user-supplied text into a generated sheet safely. Risky text (leading = + - @, tab, carriage return) gets a REAL leading apostrophe stored in the value, so it stays inert even when
    /// the sheet is saved as CSV. ClosedXML treats a single leading apostrophe as Excel's quote-prefix and drops it, hence the doubled one. Reading strips one back off (ExcelFileGuard.Unneutralize).
    /// </summary>
    public static class ExcelSafeText
    {
        public static void Put(IXLCell cell, string? text)
        {
            var safe = ExcelFileGuard.Neutralize(text);
            cell.SetValue(safe.Length > 0 && safe[0] == (char)39 ? "'" + safe : safe);
        }
    }
}
