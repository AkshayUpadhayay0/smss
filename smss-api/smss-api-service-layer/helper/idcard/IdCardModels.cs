using QImage = QuestPDF.Infrastructure.Image;

namespace smss_api_service_layer.helper.idcard
{
    /// <summary>A decoded picture with its pixel size (needed to crop a photo to fill its frame without distorting it).</summary>
    public sealed record IdCardPicture(QImage Image, int Width, int Height)
    {
        public static IdCardPicture? TryCreate(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var size = ImageSize.TryRead(bytes);
            if (size == null) return null;
            try { return new IdCardPicture(QImage.FromBinaryData(bytes), size.Value.W, size.Value.H); }
            catch { return null; }
        }
    }

    /// <summary>Which optional lines a school allows on its cards (a template only shows a line it has room for).</summary>
    public sealed record IdCardOptions(bool ShowDob, bool ShowAddress, bool ShowBloodGroup, bool ShowMother)
    {
        public static readonly IdCardOptions Defaults = new(true, true, true, true);
    }

    /// <summary>The school's branding for one render: template, colours, free text, signature.</summary>
    public sealed record IdCardBrand(string TemplateCode, string Primary, string Secondary, string? Tagline, string? Website, string? Email,
        string? Terms, IdCardPicture? Signature, IdCardOptions Options);

    /// <summary>What every card of a school says about the school (its back, and the header of its front).</summary>
    public sealed record IdCardSchoolModel(string Name, IdCardPicture? Logo, string? Address, string? Phone);

    /// <summary>One card's content. Optional fields are null when the school has no such data (or switched the line off).</summary>
    public sealed record IdCardModel(
        string FullName, string ClassName, string SectionName, string? RollNumber, string AdmissionNumber, string YearName,
        IdCardPicture? Photo, string? FatherName, string? MotherName, string? GuardianName, string? GuardianRelationship, string? Contact,
        string? Dob, string? BloodGroup, string? Address, string QrPayload, bool IsSample = false)
    {
        public string ClassSection => $"{ClassName} - {SectionName}";
        public string Initials => IdCardText.InitialsOf(FullName);
    }

    public static class IdCardText
    {
        public static string InitialsOf(string fullName)
        {
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return "?";
            static string First(string s) => new System.Globalization.StringInfo(s).SubstringByTextElements(0, 1).ToUpperInvariant();
            return parts.Length == 1 ? First(parts[0]) : First(parts[0]) + First(parts[^1]);
        }
    }

    /// <summary>Reads the pixel size of a PNG / JPEG / WEBP from its header, with no image library.</summary>
    internal static class ImageSize
    {
        public static (int W, int H)? TryRead(byte[] b)
        {
            try
            {
                if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50) // PNG: IHDR
                    return (BE32(b, 16), BE32(b, 20));
                if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8) return Jpeg(b);
                if (b.Length > 30 && b[0] == 0x52 && b[1] == 0x49 && b[8] == 0x57) return Webp(b);
            }
            catch { /* fall through */ }
            return null;
        }

        private static int BE32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

        private static (int, int)? Jpeg(byte[] b)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                var len = (b[i + 2] << 8) | b[i + 3];
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                    return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                i += 2 + len;
            }
            return null;
        }

        private static (int, int)? Webp(byte[] b)
        {
            var tag = System.Text.Encoding.ASCII.GetString(b, 12, 4);
            if (tag == "VP8X") return (1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16)));
            if (tag == "VP8L") { var v = b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24); return ((v & 0x3FFF) + 1, ((v >> 14) & 0x3FFF) + 1); }
            if (tag == "VP8 ") return ((b[26] | (b[27] << 8)) & 0x3FFF, (b[28] | (b[29] << 8)) & 0x3FFF);
            return null;
        }
    }
}
