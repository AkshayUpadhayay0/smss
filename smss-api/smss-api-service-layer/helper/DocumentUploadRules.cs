namespace smss_api_service_layer.helper
{
    public static class DocumentRules
    {
        public const long MaxBytes = 5 * 1024 * 1024;           // 5 MB
        public const long MaxRequestBytes = 6 * 1024 * 1024;     // a little above, for multipart overhead
        public static readonly string[] AllowedExtensions = { ".pdf", ".png", ".jpg", ".jpeg", ".webp" };

        public static string ContentTypeOf(string extension) => extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    /// <summary>A document that passed every upload check, buffered and rewound.</summary>
    public sealed record ValidatedDocument(MemoryStream Content, string Extension, long Length) : IDisposable
    {
        public void Dispose() => Content.Dispose();
    }

    /// <summary>Same idea as ImageUploadValidator, for documents: PDF or an image, within 5 MB, and the real file signature must match the extension.</summary>
    public static class DocumentUploadValidator
    {
        public static async Task<(ValidatedDocument? Document, string? Error)> ValidateAsync(Stream content, string fileName, long length, CancellationToken ct)
        {
            if (length <= 0) return (null, "The file is empty.");
            if (length > DocumentRules.MaxBytes) return (null, "A document must be 5 MB or smaller.");

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!DocumentRules.AllowedExtensions.Contains(extension)) return (null, "Only PDF, PNG, JPG or WEBP files are allowed.");

            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (buffer.Length > DocumentRules.MaxBytes || buffer.Length == 0)
            {
                buffer.Dispose();
                return (null, buffer.Length == 0 ? "The file is empty." : "A document must be 5 MB or smaller.");
            }

            var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 12)).ToArray();
            var ok = extension == ".pdf" ? header.Length >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D   // "%PDF-"
                                         : LogoRules.SignatureMatches(extension, header);
            if (!ok)
            {
                buffer.Dispose();
                return (null, "The file is not a valid PDF, PNG, JPG or WEBP.");
            }

            buffer.Position = 0;
            return (new ValidatedDocument(buffer, extension, buffer.Length), null);
        }
    }
}
