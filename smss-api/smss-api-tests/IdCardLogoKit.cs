using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using smss_api_service_layer.helper;
using smss_api_service_layer.helper.idcard;

namespace smss_api_tests
{
    /// <summary>Test logos (real PNGs of a chosen shape) and the card fixtures the ID card logo tests share.</summary>
    internal static class IdCardLogoKit
    {

        /// <summary>A PNG of the given pixel size: a red shield-on-white crest that fills its whole box edge to edge, so any stretch or crop is measurable.</summary>
        public static byte[] LogoPng(int w, int h, string bg = "#FFFFFF", string fg = "#7B1FA2", string dot = "#FF00FF")
        {
            PdfBootstrap.Ensure();
            var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" preserveAspectRatio=\"none\">"
                + $"<rect width=\"{w}\" height=\"{h}\" fill=\"{bg}\"/>"
                + $"<rect x=\"0\" y=\"0\" width=\"{w}\" height=\"{h * 0.12}\" fill=\"{fg}\"/><rect x=\"0\" y=\"{h * 0.88}\" width=\"{w}\" height=\"{h * 0.12}\" fill=\"{fg}\"/>"
                + $"<rect x=\"0\" y=\"0\" width=\"{w * 0.1}\" height=\"{h}\" fill=\"{fg}\"/><rect x=\"{w * 0.9}\" y=\"0\" width=\"{w * 0.1}\" height=\"{h}\" fill=\"{fg}\"/>"
                + $"<circle cx=\"{w / 2.0}\" cy=\"{h / 2.0}\" r=\"{Math.Min(w, h) * 0.28}\" fill=\"{dot}\"/></svg>";
            var doc = Document.Create(d => d.Page(p =>
            {
                p.Size(new PageSize(w, h, Unit.Point));
                p.Margin(0);
                p.Content().Svg(svg);
            }));
            return doc.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 72 }).First();
        }

        public static readonly byte[] Wide = LogoPng(400, 100);
        public static readonly byte[] Tall = LogoPng(100, 400);
        public static readonly byte[] Square = LogoPng(240, 240);

        public static IdCardSchoolModel School(byte[]? logo, string name = "Greenfield Public School") =>
            new(name, IdCardPicture.TryCreate(logo), "12 Rajpur Road, Dehradun - 248001", "0135 2740 000");

        public static IdCardBrand Brand(string template, IdCardPalette? _ = null)
        {
            var t = IdCardTemplates.Find(template)!;
            return new IdCardBrand(template, t.DefaultPrimary, t.DefaultSecondary, "Learn · Lead · Serve", "https://school.test", "info@school.test",
                "Carry this card at all times.\nReport loss to the office.", null, IdCardOptions.Defaults);
        }

        public static IdCardModel Card(bool photo = false) =>
            new("Aarav Sharma", "Class 5", "A", "12", "ADM2026001", "2026-27", photo ? IdCardPicture.TryCreate(LogoPng(300, 400, "#00FF00", "#00FF00", "#00FF00")) : null,
                "Rajesh Sharma", "Sunita Sharma", "Rajesh Sharma", "Father", "98765 43210", "14 Aug 2015", "B+", "12 Rajpur Road, Dehradun - 248001",
                IdCardQr.Payload("12345678910", "ADM2026001"), IsSample: true);

        public static readonly string[] Templates = IdCardTemplates.All.Select(t => t.Code).ToArray();
    }
}
