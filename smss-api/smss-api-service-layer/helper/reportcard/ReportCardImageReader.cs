using Microsoft.Extensions.Logging;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// Reads the two images a card can carry, at the moment it is drawn (the card model only stores KEYS, never bytes):
    /// the school's logo (the school record's public /uploads path) and the principal's signature (a key in the PRIVATE store). A missing or unreadable file is simply no image.
    /// </summary>
    public sealed class ReportCardImageReader
    {
        private const long MaxImageBytes = 2_000_000;
        private const long MaxPhotoBytes = 3_000_000;
        private readonly IFileStorageService _storage;
        private readonly ILogger<ReportCardImageReader> _logger;
        public ReportCardImageReader(IFileStorageService storage, ILogger<ReportCardImageReader> logger) { _storage = storage; _logger = logger; }

        public byte[]? Logo(string? uploadPath) => Read(uploadPath, privateStore: false);
        public byte[]? Signature(string? privateKey) => Read(privateKey, privateStore: true);

        /// <summary>
        /// The student's photo, read at the moment the card is drawn through the same server-side path as the student photo screen and the ID card (a private key, or a not-yet-migrated public
        /// path). Never part of a stored card. A missing or unreadable photo is simply no photo (the design draws a neutral placeholder). The picture is scaled down to about 300 px by the PDF
        /// writer when it is drawn (the slot sets its raster resolution), so a large camera photo does not make the file large.
        /// </summary>
        public byte[]? Photo(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return null;
            try
            {
                using var stream = _storage.OpenPhoto(stored);
                if (stream == null) return null;
                using var ms = new MemoryStream(); stream.CopyTo(ms);
                return ms.Length is > 0 and <= MaxPhotoBytes ? ms.ToArray() : null;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not read a student photo for a report card"); return null; }
        }

        private byte[]? Read(string? key, bool privateStore)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            try
            {
                using var stream = privateStore ? _storage.OpenPrivate(key) : _storage.OpenUpload(key);
                if (stream == null) return null;
                using var ms = new MemoryStream(); stream.CopyTo(ms);
                return ms.Length is > 0 and <= MaxImageBytes ? ms.ToArray() : null;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not read an image for a report card"); return null; }
        }

        /// <summary>Both images of one card, ready for the template.</summary>
        public RcContext Context(ReportCardModel model, bool draft, string? signatureKeyFallback = null) =>
            new() { Model = model, Logo = Logo(model.Header.LogoKey), Signature = Signature(model.SignatureKey ?? signatureKeyFallback), Photo = model.ShowPhoto ? Photo(model.Student.PhotoKey) : null, Draft = draft };
    }

    /// <summary>Applies a school's settings to a card model (used by the live preview, where the settings on screen are not saved yet).</summary>
    public static class ReportCardBranding
    {
        public static void Apply(ReportCardModel m, ReportCardSettingsClean s, string? signatureKey)
        {
            m.Primary = s.PrimaryColor; m.Secondary = s.SecondaryColor;
            m.Header.Line = s.HeaderLine; m.Header.Recognition = s.RecognitionText; m.Header.Udise = s.UdiseCode;
            m.Footer = s.FooterText; m.RulesText = s.RulesText; m.SignatureKey = signatureKey;
            m.ShowCover = s.ShowCover; m.ShowRules = s.ShowRules; m.ShowAttendance = s.ShowAttendance; m.ShowGradeLegend = s.ShowGradeLegend; m.ShowPhoto = s.ShowPhoto;
        }
    }
}
