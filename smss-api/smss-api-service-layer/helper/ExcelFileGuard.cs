using System.IO.Compression;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// Checks an uploaded workbook BEFORE a spreadsheet library ever opens it: it is a real .xlsx (zip signature), small, not a
    /// macro workbook, carries no embedded objects or links to other files, and does not decompress into something huge (zip bomb).
    /// The decompression check actually reads every entry up to a hard cap: the sizes written in a zip header can be forged.
    /// </summary>
    public static class ExcelFileGuard
    {
        public const int MaxFileBytes = 1024 * 1024;               // 1 MB
        public const long MaxUncompressedBytes = 25L * 1024 * 1024; // all parts together
        public const long MaxEntryBytes = 12L * 1024 * 1024;        // any single part
        public const int MaxEntries = 100;
        public const int MaxRatio = 200;                            // uncompressed : compressed

        /// <summary>Null when the file is acceptable, otherwise a calm sentence for the user.</summary>
        public static string? Inspect(byte[] bytes)
        {
            if (bytes.Length == 0) return "The file is empty.";
            if (bytes.Length > MaxFileBytes) return "The file is larger than 1 MB. Split it into smaller files.";
            if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B || bytes[2] != 0x03 || bytes[3] != 0x04)
                return "That is not an .xlsx file. Download the template, fill it in and save it as an Excel Workbook (.xlsx).";

            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
                if (zip.Entries.Count > MaxEntries) return "The file has too many parts to be a normal workbook.";

                long total = 0;
                var names = new List<string>();
                var buffer = new byte[81920];
                foreach (var e in zip.Entries)
                {
                    names.Add(e.FullName);
                    var lower = e.FullName.ToLowerInvariant();
                    if (lower.StartsWith("xl/vbaproject") || lower.EndsWith("vbaproject.bin") || lower.StartsWith("xl/macrosheets") || lower.Contains("/vbadata"))
                        return "Workbooks with macros are not accepted. Save the file as an Excel Workbook (.xlsx) without macros.";
                    if (lower.StartsWith("xl/externallinks/")) return "The file links to other workbooks, which is not allowed. Paste values into the template instead.";
                    if (lower.StartsWith("xl/embeddings/") || lower.StartsWith("xl/activex/")) return "The file contains embedded objects, which are not allowed.";

                    long entryBytes = 0;
                    using var s = e.Open();
                    int n;
                    while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        entryBytes += n; total += n;
                        if (entryBytes > MaxEntryBytes || total > MaxUncompressedBytes) return "The file is far larger than it looks once unpacked and cannot be read.";
                    }
                }
                if (bytes.Length > 0 && total / Math.Max(1, bytes.Length) > MaxRatio) return "The file is far larger than it looks once unpacked and cannot be read.";

                if (!names.Contains("[Content_Types].xml") || !names.Contains("xl/workbook.xml"))
                    return "That is not an Excel workbook. Download the template, fill it in and save it as .xlsx.";

                var ct = zip.GetEntry("[Content_Types].xml")!;
                using (var r = new StreamReader(ct.Open()))
                {
                    var text = r.ReadToEnd();
                    if (text.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) || text.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))
                        return "Workbooks with macros are not accepted. Save the file as an Excel Workbook (.xlsx) without macros.";
                }
                return null;
            }
            catch (InvalidDataException) { return "The file is damaged or is not a real .xlsx workbook."; }
            catch (Exception) { return "The file could not be read."; }
        }

        /// <summary>
        /// Text that starts with = + - or @ (or a tab / carriage return) can be run as a formula when someone opens a generated sheet
        /// in a spreadsheet program or re-exports it to CSV. Such text gets a leading apostrophe. Reading strips one back off.
        /// </summary>
        public static string Neutralize(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var c = s[0];
            return c is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s;
        }

        public static string Unneutralize(string s) => s.Length > 1 && s[0] == '\'' && s[1] is '=' or '+' or '-' or '@' or '\t' or '\r' ? s[1..] : s;
    }
}
