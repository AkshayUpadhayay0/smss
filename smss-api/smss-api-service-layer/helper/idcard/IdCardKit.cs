using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace smss_api_service_layer.helper.idcard
{
    /// <summary>Where the student photo goes: a circle or a rounded rectangle, in millimetres on the 54 x 85.6 mm card.</summary>
    public readonly record struct Spot(bool Round, float X, float Y, float W, float H, float Radius = 0)
    {
        public static Spot Circle(float cx, float cy, float r) => new(true, cx - r, cy - r, 2 * r, 2 * r, r);
        public static Spot Box(float x, float y, float w, float h, float radius = 0) => new(false, x, y, w, h, radius);
        public float Cx => X + W / 2;
        public float Cy => Y + H / 2;

        /// <summary>The spot as an SVG shape in card units (1 mm = 10 units), optionally grown by <paramref name="growMm"/> on every side.</summary>
        public string Shape(string attrs, float growMm = 0)
        {
            var g = growMm;
            if (Round)
                return $"<circle cx=\"{Kit.U(Cx)}\" cy=\"{Kit.U(Cy)}\" r=\"{Kit.U(W / 2 + g)}\" {attrs}/>";
            return $"<rect x=\"{Kit.U(X - g)}\" y=\"{Kit.U(Y - g)}\" width=\"{Kit.U(W + 2 * g)}\" height=\"{Kit.U(H + 2 * g)}\" rx=\"{Kit.U(Radius + g)}\" {attrs}/>";
        }
    }

    public enum Align { Left, Center, Right }

    /// <summary>Everything one face of one card needs to draw itself.</summary>
    public sealed class CardCtx
    {
        public required IdCardPalette Pal { get; init; }
        public required IdCardModel Card { get; init; }
        public required IdCardSchoolModel School { get; init; }
        public required IdCardBrand Brand { get; init; }
        public IdCardOptions Opt => Brand.Options;

        /// <summary>Label / value pairs for the keys asked for, skipping lines the school switched off and fields this student has no data for.</summary>
        public List<(string Label, string Value)> Rows(params string[] keys)
        {
            var rows = new List<(string, string)>();
            void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add((label, Clip(value!.Trim(), label == "Address" ? 80 : 56))); }
            var hasParent = !string.IsNullOrWhiteSpace(Card.FatherName) || !string.IsNullOrWhiteSpace(Card.MotherName);
            foreach (var k in keys)
                switch (k)
                {
                    case "name": Add("Name", Card.FullName); break;
                    case "adm": Add("Adm. No", Card.AdmissionNumber); break;
                    case "roll": Add("Roll No", Card.RollNumber); break;
                    case "class": Add("Class", Card.ClassSection); break;
                    case "session": Add("Session", Card.YearName); break;
                    case "father":
                        if (!string.IsNullOrWhiteSpace(Card.FatherName)) Add("Father", Card.FatherName);
                        else if (!hasParent) Add(string.IsNullOrWhiteSpace(Card.GuardianRelationship) ? "Guardian" : Title(Card.GuardianRelationship!), Card.GuardianName);
                        break;
                    case "mother": if (Opt.ShowMother) Add("Mother", Card.MotherName); break;
                    case "contact": Add("Contact", Card.Contact); break;
                    case "dob": if (Opt.ShowDob) Add("DOB", Card.Dob); break;
                    case "blood": if (Opt.ShowBloodGroup) Add("Blood Grp", Card.BloodGroup); break;
                    case "address": if (Opt.ShowAddress) Add("Address", Card.Address); break;
                }
            return rows;
        }

        /// <summary>A very long value (a whole postal address, a four-part name) is cut with an ellipsis so one line cannot shrink the rest of the card to unreadable.</summary>
        private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd(' ', ',') + "…";

        private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

        /// <summary>Address / phone / e-mail / website of the school, one entry each, only those that exist.</summary>
        public List<(string Label, string Value)> SchoolContact()
        {
            var l = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(School.Address)) l.Add(("Address", School.Address!));
            if (!string.IsNullOrWhiteSpace(School.Phone)) l.Add(("Phone", School.Phone!));
            if (!string.IsNullOrWhiteSpace(Brand.Email)) l.Add(("Email", Brand.Email!));
            if (!string.IsNullOrWhiteSpace(Brand.Website)) l.Add(("Web", Brand.Website!));
            return l;
        }

        public List<string> TermsLines() =>
            (Brand.Terms ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5).ToList();
    }

    /// <summary>Small drawing helpers shared by the six templates.</summary>
    public static class Kit
    {
        public const Unit Mm = Unit.Millimetre;
        public const string Sans = "Hind";
        public const string Serif = "PT Serif";

        public static string U(double mm) => (mm * 10).ToString("0.##", CultureInfo.InvariantCulture);
        public static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>A layer positioned in millimetres.</summary>
        public static IContainer At(this LayersDescriptor l, float x, float y, float w, float h) =>
            l.Layer().TranslateX(x, Mm).TranslateY(y, Mm).Width(w, Mm).Height(h, Mm);

        /// <summary>
        /// The card art: one SVG covering the whole card (1 mm = 10 units). Everything inside the group is cut by a mask so the photo drawn
        /// BEHIND the art shows through the photo spot; rings and other overlays go in <paramref name="overlay"/>, above the mask.
        /// </summary>
        public static string Art(Spot spot, string art, string overlay = "") =>
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 540 856\" preserveAspectRatio=\"none\">"
            + "<defs><mask id=\"hole\" maskUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"540\" height=\"856\"><rect width=\"540\" height=\"856\" fill=\"#fff\"/>"
            + spot.Shape("fill=\"#000\"") + "</mask></defs>"
            + "<g mask=\"url(#hole)\">" + art + "</g>" + overlay + "</svg>";

        /// <summary>Art with no photo spot (the back of a card).</summary>
        public static string Plain(string art) =>
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 540 856\" preserveAspectRatio=\"none\">" + art + "</svg>";

        public static string Rect(double x, double y, double w, double h, string fill, string extra = "") =>
            $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" fill=\"{fill}\" {extra}/>";
        public static string Circle(double cx, double cy, double r, string fill, string extra = "") =>
            $"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N(r)}\" fill=\"{fill}\" {extra}/>";
        public static string Path(string d, string fill, string extra = "") => $"<path d=\"{d}\" fill=\"{fill}\" {extra}/>";
        public static string Stroke(string shape) => shape;

        /// <summary>Outline for a shape drawn in a very pale brand colour, so it still shows on white.</summary>
        public static string PaleEdge(bool pale, string color) => pale ? $"stroke=\"{color}\" stroke-opacity=\".35\" stroke-width=\"3\"" : "";

        /// <summary>A scalloped rosette (the grade badge): a ring of bumps around a solid disc, as one SVG polygon.</summary>
        public static string Rosette(double cx, double cy, double r, string fill, int teeth = 18)
        {
            var pts = new System.Text.StringBuilder();
            for (var i = 0; i < teeth * 2; i++)
            {
                var a = Math.PI * i / teeth;
                var rr = i % 2 == 0 ? r : r * 0.9;
                pts.Append(N(cx + rr * Math.Cos(a))).Append(',').Append(N(cy + rr * Math.Sin(a))).Append(' ');
            }
            return $"<polygon points=\"{pts}\" fill=\"{fill}\"/>";
        }

        /// <summary>The number in a class name ("Class 5" -> "5", "UKG" -> "UKG"), for the grade badge.</summary>
        public static string GradeLabel(string className)
        {
            var digits = new string(className.Where(char.IsDigit).ToArray());
            if (digits.Length is > 0 and <= 2) return digits;
            var word = className.Trim();
            return word.Length <= 4 ? word.ToUpperInvariant() : word[..3].ToUpperInvariant();
        }

        // ---- the photo ----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Draws the photo (cropped to FILL the spot, biased to the top so faces stay in), or a placeholder: a vector silhouette for the
        /// built-in sample, the initials background for a real student without a photo. It goes BELOW the art; the art's hole reveals it.
        /// </summary>
        public static void Backdrop(LayersDescriptor l, CardCtx x, Spot s)
        {
            var photo = x.Card.Photo;
            if (photo != null)
            {
                var photoAspect = (float)photo.Width / photo.Height;
                var spotAspect = s.W / s.H;
                float w, h;
                if (photoAspect > spotAspect) { h = s.H; w = h * photoAspect; } else { w = s.W; h = w / photoAspect; }
                var px = s.X - (w - s.W) / 2;
                var py = s.Y - (h - s.H) * 0.2f;
                l.Layer().TranslateX(px, Mm).TranslateY(py, Mm).Width(w, Mm).Height(h, Mm).Image(photo.Image).FitUnproportionally();
                return;
            }

            var pal = x.Pal;
            var bg = IdCardPalette.Mix(pal.Primary, "#FFFFFF", 0.82);
            var fg = IdCardPalette.Mix(pal.Primary, "#FFFFFF", 0.35);
            var svg = x.Card.IsSample
                ? "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" preserveAspectRatio=\"xMidYMid slice\">"
                  + $"<rect width=\"100\" height=\"100\" fill=\"{bg}\"/><circle cx=\"50\" cy=\"38\" r=\"17\" fill=\"{fg}\"/>"
                  + $"<path d=\"M12 100C12 70 30 58 50 58C70 58 88 70 88 100Z\" fill=\"{fg}\"/></svg>"
                : $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" preserveAspectRatio=\"none\"><rect width=\"100\" height=\"100\" fill=\"{bg}\"/></svg>";
            l.At(s.X, s.Y, s.W, s.H).Svg(svg);
        }

        /// <summary>Initials on the placeholder (only for a real student without a photo).</summary>
        public static void InitialsOver(LayersDescriptor l, CardCtx x, Spot s)
        {
            if (x.Card.Photo != null || x.Card.IsSample) return;
            var size = Math.Max(14, Math.Min(s.W, s.H) * 1.35f);
            l.At(s.X, s.Y, s.W, s.H).AlignMiddle().AlignCenter()
                .Text(x.Card.Initials).FontFamily(Sans).FontSize(size).Bold().FontColor(x.Pal.PrimaryText).LineHeight(1f);
        }

        // ---- text ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Text in a fixed box. It wraps inside the box width and, if it still does not fit, the whole block is scaled down
        /// (so a very long name or address shrinks instead of overflowing or being cut).
        /// </summary>
        public static void Fit(IContainer box, string text, float size, string color, Align align = Align.Center, bool bold = false, bool semi = false,
            bool serif = false, bool upper = false, float spacing = 0, bool middle = true, float lineHeight = 1.05f)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var c = middle ? box.AlignMiddle() : box.AlignTop();
            c = align switch { Align.Left => c.AlignLeft(), Align.Right => c.AlignRight(), _ => c.AlignCenter() };
            var span = c.ScaleToFit().Text(upper ? text.ToUpperInvariant() : text)
                .FontFamily(serif ? new[] { Serif, Sans } : new[] { Sans }).FontSize(size).FontColor(color).LineHeight(lineHeight);
            if (bold) span = span.Bold(); else if (semi) span = span.SemiBold();
            if (spacing != 0) span = span.LetterSpacing(spacing);
            _ = align switch { Align.Left => span.AlignLeft(), Align.Right => span.AlignRight(), _ => span.AlignCenter() };
        }

        /// <summary>A block of "Label  value" rows. Values wrap; the whole block shrinks to fit the box.</summary>
        public static void Rows(IContainer box, IReadOnlyList<(string Label, string Value)> rows, float labelWidth, float size, string labelColor, string valueColor,
            bool serif = false, float gap = 0.35f, bool upperLabels = false)
        {
            if (rows.Count == 0) return;
            var fam = serif ? new[] { Serif, Sans } : new[] { Sans };
            box.AlignTop().ScaleToFit().Column(col =>
            {
                col.Spacing(gap, Mm);
                foreach (var (label, value) in rows)
                    col.Item().Row(r =>
                    {
                        r.ConstantItem(labelWidth, Mm).Text(upperLabels ? label.ToUpperInvariant() : label).FontFamily(fam).FontSize(size * (upperLabels ? 0.85f : 1f)).FontColor(labelColor).LineHeight(1.05f);
                        r.ConstantItem(1.5f, Mm).Text(":").FontFamily(fam).FontSize(size).FontColor(labelColor).LineHeight(1.05f);
                        r.RelativeItem().Text(value).FontFamily(fam).FontSize(size).FontColor(valueColor).SemiBold().LineHeight(1.05f);
                    });
            });
        }

        /// <summary>
        /// The school logo on a light plate (white, or the template's own tint), or nothing when the school has none. The plate is a rounded square whose corner radius is smaller
        /// than its padding allows to clip, so the logo is never cropped; the image keeps its aspect ratio and is centred inside the box (FitArea), never stretched.
        /// </summary>
        public static void Logo(IContainer box, CardCtx x, string tile = "#FFFFFF", bool round = true)
        {
            var logo = x.School.Logo;
            if (logo == null) return;
            // padding (0.9 mm) is more than the 0.29 x radius a corner can cut into a square image, so even a full-bleed logo keeps its corners
            box.Background(tile).CornerRadius(round ? 1.5f : 1.0f).Padding(0.9f, Mm).Image(logo.Image).FitArea();
        }

        public static bool HasLogo(CardCtx x) => x.School.Logo != null;

        /// <summary>The QR code (white tile, quiet zone) and its readable reference under it.</summary>
        public static void Qr(LayersDescriptor l, CardCtx x, float cx, float y, float size, string captionColor, string frame = "#FFFFFF")
        {
            var pad = 1.3f;
            var tile = size + 2 * pad;
            l.At(cx - tile / 2, y - pad, tile, tile).Background(frame).CornerRadius(1.4f).Padding(pad, Mm).Svg(IdCardQr.Svg(x.Card.QrPayload));
        }

        public static void QrCaption(IContainer box, CardCtx x, string color, float size = 4.6f)
        {
            box.AlignCenter().AlignMiddle().ScaleToFit().Text(x.Card.QrPayload.Replace(IdCardQr.Separator, " / ")).FontFamily(Sans).FontSize(size).FontColor(color).LetterSpacing(0.03f);
        }

        /// <summary>Signature image (if the school uploaded one) over a rule, with a caption underneath.</summary>
        public static void Signature(LayersDescriptor l, CardCtx x, float bx, float by, float bw, float bh, string lineColor, string captionColor, float captionSize = 5.6f, bool serif = false)
        {
            var sig = x.Brand.Signature;
            var imgH = bh - 3.2f;
            if (sig != null) l.At(bx, by, bw, imgH).AlignBottom().AlignCenter().Image(sig.Image).FitArea();
            l.At(bx, by + imgH, bw, 0.25f).Background(lineColor);
            Fit(l.At(bx, by + imgH + 0.4f, bw, 2.6f), "Principal", captionSize, captionColor, Align.Center, serif: serif);
        }

        /// <summary>The school's terms as short bullet lines (or a default one-liner when the school wrote none).</summary>
        public static void Terms(IContainer box, CardCtx x, float size, string color, string bullet, bool serif = false)
        {
            var lines = x.TermsLines();
            if (lines.Count == 0) lines = new List<string> { "If found, please return this card to the school." };
            var fam = serif ? new[] { Serif, Sans } : new[] { Sans };
            box.AlignTop().ScaleToFit().Column(col =>
            {
                col.Spacing(0.5f, Mm);
                foreach (var line in lines)
                    col.Item().Row(r =>
                    {
                        r.ConstantItem(2.2f, Mm).Text(bullet).FontFamily(fam).FontSize(size).FontColor(color).Bold();
                        r.RelativeItem().Text(line).FontFamily(fam).FontSize(size).FontColor(color).LineHeight(1.08f);
                    });
            });
        }

        /// <summary>School contact lines (address, phone, email, web), label in muted colour, value in the text colour.</summary>
        public static void Contact(IContainer box, CardCtx x, float size, string labelColor, string valueColor, float labelWidth = 9.5f, bool serif = false)
        {
            var rows = x.SchoolContact();
            if (rows.Count == 0) return;
            Rows(box, rows, labelWidth, size, labelColor, valueColor, serif, 0.5f);
        }
    }
}
