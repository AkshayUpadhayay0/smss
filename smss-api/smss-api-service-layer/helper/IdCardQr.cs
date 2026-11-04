using QRCoder;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// The QR on the back of a student ID card. It carries ONLY a plain reference - school code + admission number - and nothing else:
    /// no name, date of birth, phone, Aadhaar, student id, or URL. It is a lookup key a staff member types into the app, not a link.
    /// </summary>
    public static class IdCardQr
    {
        public const string Separator = ":";

        public static string Payload(string schoolCode, string admissionNumber) =>
            $"{schoolCode.Trim()}{Separator}{admissionNumber.Trim()}";

        /// <summary>The code as vector SVG (no quiet zone drawn here - the card leaves its own white margin).</summary>
        public static string Svg(string payload)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            return new SvgQRCode(data).GetGraphic(4, "#000000", "#FFFFFF", drawQuietZones: false, SvgQRCode.SizingMode.ViewBoxAttribute);
        }
    }
}
