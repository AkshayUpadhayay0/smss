using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.idcard.Kit;

namespace smss_api_service_layer.helper.idcard
{
    public abstract class IdCardTemplate
    {
        public abstract string Code { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string DefaultPrimary { get; }
        public abstract string DefaultSecondary { get; }
        public abstract void Front(IContainer c, CardCtx x);
        public abstract void Back(IContainer c, CardCtx x);

        protected static string Op(string color, double opacity) => $"fill-opacity=\"{N(opacity)}\"";
    }

    /// <summary>The six built-in designs. Codes are constants (no lookup table): a school stores only the code.</summary>
    public static class IdCardTemplates
    {
        public const string DefaultCode = "classic-blue";

        public static readonly IReadOnlyList<IdCardTemplate> All = new IdCardTemplate[]
        {
            new ClassicBlue(), new TealGeometric(), new RedCurve(), new PlayfulBlob(), new ModernNavy(), new FormalClassic(),
        };

        public static IdCardTemplate? Find(string? code) => All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));
        public static bool IsValid(string? code) => Find(code) != null;
        public static IdCardTemplate Default => All[0];
    }

    // =================================================================================================================================
    // 1. classic-blue: blue curved header band, big circular photo over the band, label/value list, back with contact + terms + QR
    // =================================================================================================================================
    internal sealed class ClassicBlue : IdCardTemplate
    {
        public override string Code => "classic-blue";
        public override string Name => "Classic Blue";
        public override string Description => "Curved header band with a large round photo and a clean details list.";
        public override string DefaultPrimary => "#1E5BD8";
        public override string DefaultSecondary => "#F5A623";
        private static readonly Spot Photo = Spot.Circle(27, 33.2f, 11f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V240C420 304 120 304 0 240Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 240C120 304 420 304 540 240V262C420 326 120 326 0 262Z", p.PrimaryDark, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Circle(505, 30, 95, "#FFFFFF", "fill-opacity=\".09\"") + Circle(20, 170, 70, "#FFFFFF", "fill-opacity=\".08\"")
                + Rect(0, 808, 540, 48, p.Primary) + Rect(0, 804, 540, 5, p.Secondary);
            var ring = Photo.Shape("fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"14\"", 0.7f) + Photo.Shape($"fill=\"none\" stroke=\"{p.PrimaryDark}\" stroke-width=\"7\"", 1.7f);
            var tag = !string.IsNullOrWhiteSpace(x.Brand.Tagline);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(2.8f, 2.6f, 8, 8), x);
                var nameX = HasLogo(x) ? 12f : 3f;
                Fit(l.At(nameX, 2.2f, 54 - nameX - 3, tag ? 10.6f : 14), x.School.Name, 10.8f, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, bold: true);
                if (tag) Fit(l.At(nameX, 12.9f, 54 - nameX - 3, 3.6f), x.Brand.Tagline!, 6.2f, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, spacing: 0.06f);
                Fit(l.At(3, 46.4f, 48, 5.6f), x.Card.FullName, 12.6f, p.PrimaryText, bold: true);
                l.At(14, 52.4f, 26, 4.6f).Background(p.Secondary).CornerRadius(100).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text(x.Card.ClassSection).FontFamily(Sans).FontSize(7.8f).SemiBold().FontColor(p.OnSecondary).AlignCenter();
                Rows(l.At(5.5f, 59f, 43, 21.4f), x.Rows("adm", "roll", "father", "contact", "dob", "blood"), 12.5f, 6.8f, p.Muted, p.Ink);
                Fit(l.At(0, 81.2f, 54, 4.4f), $"Session {x.Card.YearName}", 6.4f, p.OnPrimary, semi: true);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V46C400 82 140 82 0 46Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 856V772C130 726 330 762 540 698V856Z", p.PrimaryDark, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 772C130 726 330 762 540 698V712C330 776 130 740 0 786Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Circle(500, 20, 70, "#FFFFFF", "fill-opacity=\".08\"");
            var onDark = IdCardPalette.On(p.PrimaryDark);
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                Fit(l.At(4, 0.6f, 46, 4.6f), x.Brand.Tagline ?? "", 5.8f, p.OnPrimary, semi: true, spacing: 0.06f);
                l.At(8, 11.2f, 38, 5.4f).Background(p.Primary).CornerRadius(0.8f).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text("TERMS AND CONDITIONS").FontFamily(Sans).FontSize(6.6f).Bold().FontColor(p.OnPrimary).LetterSpacing(0.06f).AlignCenter();
                Terms(l.At(6.5f, 18.6f, 41, 13.6f), x, 5.6f, p.Ink, "•");
                Rows(l.At(6.5f, 33.6f, 41, 15f), x.SchoolContact(), 8.5f, 6.2f, p.Muted, p.Ink);
                Signature(l, x, 16, 49.6f, 22, 11.6f, p.Ink, p.Muted);
                Fit(l.At(6.5f, 62.2f, 41, 3.6f), $"Valid for session {x.Card.YearName}", 5.8f, p.PrimaryText, semi: true);
                if (HasLogo(x)) Logo(l.At(4.5f, 73.8f, 10, 10), x);
                Fit(l.At(HasLogo(x) ? 15.5f : 4.5f, 73.6f, HasLogo(x) ? 19 : 27, 10.4f), x.School.Name, 7.2f, onDark, Align.Left, bold: true);
                Qr(l, x, 43.8f, 71.4f, 11.2f, onDark);
                Fit(l.At(32.5f, 83.4f, 22, 1.6f), x.Card.QrPayload.Replace(IdCardQr.Separator, " / "), 3.1f, onDark);
            });
        }
    }

    // =================================================================================================================================
    // 2. teal-geometric: white card, angular teal + yellow shapes, centred round photo, contact block; back mostly graphic
    // =================================================================================================================================
    internal sealed class TealGeometric : IdCardTemplate
    {
        public override string Code => "teal-geometric";
        public override string Name => "Teal Geometric";
        public override string Description => "Angular shapes on white with a centred round photo.";
        public override string DefaultPrimary => "#0E9F94";
        public override string DefaultSecondary => "#F4B400";
        private static readonly Spot Photo = Spot.Circle(27, 39.5f, 10.5f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V150L0 232Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 0H300L0 140Z", p.PrimaryLight, "fill-opacity=\".45\"")
                + Path("M0 232L540 150V176L0 258Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Path("M540 0V74L446 0Z", p.Secondary)
                + Path("M0 856V814L540 788V856Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 814L540 788V800L0 826Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Path("M470 856L540 830V856Z", p.PrimaryLight, "fill-opacity=\".5\"");
            var ring = Photo.Shape($"fill=\"none\" stroke=\"{p.Primary}\" stroke-width=\"9\"", 0.5f) + Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"3.5\"", 1.5f);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(4, 3, 9.5f, 9.5f), x);
                Fit(l.At(HasLogo(x) ? 15 : 4, 2.5f, HasLogo(x) ? 35 : 46, 10.5f), x.School.Name, 9.6f, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, bold: true, serif: true);
                Fit(l.At(3, 52.5f, 48, 6), x.Card.FullName, 12.5f, p.PrimaryText, bold: true);
                Fit(l.At(3, 58.4f, 48, 4), x.Card.ClassSection, 8, p.Ink, semi: true, spacing: 0.04f);
                var pair = new List<(string, string)>();
                var rows = x.Rows("adm", "roll", "father", "contact");
                var dobBlood = string.Join("   ", new[] { x.Rows("dob").FirstOrDefault().Value, x.Rows("blood").FirstOrDefault().Value }.Where(v => !string.IsNullOrEmpty(v)));
                if (dobBlood.Length > 0) rows.Add(("DOB / BG", dobBlood));
                Rows(l.At(6.5f, 63.6f, 41, 16), rows, 13, 6.4f, p.Muted, p.Ink);
                Fit(l.At(0, 81.6f, 54, 3.6f), $"Session {x.Card.YearName}", 6, p.OnPrimary, semi: true);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V330L0 420Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 0H340L0 190Z", p.PrimaryLight, "fill-opacity=\".4\"")
                + Path("M0 420L540 330V362L0 452Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Path("M540 0V90L430 0Z", p.Secondary)
                + Path("M0 856V790L540 750V856Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 790L540 750V764L0 804Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark));
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                if (HasLogo(x)) Logo(l.At(22, 5, 10, 10), x);
                Fit(l.At(4, HasLogo(x) ? 16.5f : 8, 46, HasLogo(x) ? 9 : 13), x.School.Name, 11.5f, p.OnPrimary, bold: true, serif: true);
                if (!string.IsNullOrWhiteSpace(x.Brand.Tagline)) Fit(l.At(6, 26, 42, 4), x.Brand.Tagline!, 6, p.OnPrimary, semi: true);
                Qr(l, x, 27, 36.5f, 19f, p.Muted, "#FFFFFF");
                QrCaption(l.At(5, 58.5f, 44, 2.4f), x, p.Muted);
                Contact(l.At(6.5f, 62.5f, 41, 11), x, 6, p.Muted, p.Ink);
                Fit(l.At(0, 80, 54, 4), $"Session {x.Card.YearName}", 6, p.OnPrimary, semi: true);
            });
        }
    }

    // =================================================================================================================================
    // 3. red-curve: bold curved header with school name + session, ringed photo, long details list incl. father, mother, address
    // =================================================================================================================================
    internal sealed class RedCurve : IdCardTemplate
    {
        public override string Code => "red-curve";
        public override string Name => "Bold Curve";
        public override string Description => "Bold coloured curve with a ringed photo and a full details list.";
        public override string DefaultPrimary => "#C62828";
        public override string DefaultSecondary => "#FFB300";
        private static readonly Spot Photo = Spot.Circle(27, 31.5f, 9.5f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V232C410 322 130 322 0 232Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 232C130 322 410 322 540 232V248C410 338 130 338 0 248Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Circle(500, 30, 90, "#FFFFFF", "fill-opacity=\".09\"") + Circle(30, 190, 60, "#FFFFFF", "fill-opacity=\".08\"")
                + Path("M0 856V826C140 800 380 846 540 812V856Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 826C140 800 380 846 540 812V820C380 854 140 808 0 834Z", p.Secondary);
            var ring = Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"13\"", 0.6f) + Photo.Shape("fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"6\"", 1.7f);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(3.5f, 2.6f, 9, 9), x);
                Fit(l.At(HasLogo(x) ? 13.5f : 4, 1.8f, HasLogo(x) ? 37 : 46, 8.2f), x.School.Name, 10f, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, bold: true);
                if (!string.IsNullOrWhiteSpace(x.School.Address)) Fit(l.At(HasLogo(x) ? 13.5f : 4, 10f, HasLogo(x) ? 37 : 46, 2.8f), x.School.Address!, 4.7f, IdCardPalette.Mix(p.Primary, p.OnPrimary, 0.85), HasLogo(x) ? Align.Left : Align.Center);
                l.At(15, 13.4f, 24, 4.2f).Background(p.Secondary).CornerRadius(100).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text($"SESSION {x.Card.YearName}").FontFamily(Sans).FontSize(6.2f).Bold().FontColor(p.OnSecondary).LetterSpacing(0.05f).AlignCenter();
                var plate = l.At(5, 43.4f, 44, 5.6f).Background(p.Primary).CornerRadius(0.8f);
                plate.AlignMiddle().AlignCenter().ScaleToFit().Text(x.Card.FullName).FontFamily(Sans).FontSize(11.2f).Bold().FontColor(p.OnPrimary).AlignCenter();
                Fit(l.At(3, 49.2f, 48, 3.6f), x.Card.ClassSection, 7.4f, p.Ink, semi: true);
                Rows(l.At(5, 54f, 44, 27), x.Rows("adm", "roll", "dob", "blood", "father", "mother", "contact", "address"), 11.5f, 6.3f, p.Muted, p.Ink);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V118C410 168 130 168 0 118Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 118C130 168 410 168 540 118V132C410 182 130 182 0 132Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Path("M0 856V826C140 800 380 846 540 812V856Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 826C140 800 380 846 540 812V820C380 854 140 808 0 834Z", p.Secondary);
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                if (HasLogo(x)) Logo(l.At(23, 1.4f, 8, 8), x);
                Fit(l.At(4, HasLogo(x) ? 9.4f : 3.5f, 46, HasLogo(x) ? 4.6f : 9), x.School.Name, 8.6f, p.OnPrimary, bold: true);
                Fit(l.At(5, 19.6f, 44, 3.6f), "TERMS & CONDITIONS", 6.2f, p.PrimaryText, Align.Left, bold: true, spacing: 0.08f);
                Terms(l.At(5, 23.2f, 44, 12.4f), x, 5.6f, p.Ink, "•");
                Fit(l.At(5, 37.4f, 44, 3.6f), "SCHOOL CONTACT", 6.2f, p.PrimaryText, Align.Left, bold: true, spacing: 0.08f);
                Contact(l.At(5, 41f, 44, 14), x, 6.2f, p.Muted, p.Ink);
                Qr(l, x, 40.5f, 57.8f, 14f, p.Muted);
                QrCaption(l.At(30, 73.8f, 21, 2.2f), x, p.Muted, 4f);
                Signature(l, x, 5, 60, 20, 13, p.Ink, p.Muted);
            });
        }
    }

    // =================================================================================================================================
    // 4. playful-blob: soft organic border, rounded-square photo, friendly colours; ID prominent; back with QR and address
    // =================================================================================================================================
    internal sealed class PlayfulBlob : IdCardTemplate
    {
        public override string Code => "playful-blob";
        public override string Name => "Playful Blob";
        public override string Description => "Soft organic border and friendly colours, made for primary and nursery.";
        public override string DefaultPrimary => "#FF7A45";
        public override string DefaultSecondary => "#FFC93C";
        private static readonly Spot Photo = Spot.Box(16, 19.5f, 22, 22, 6);
        private const string Blob = "M62 78C70 34 140 18 210 34C270 48 336 12 410 30C486 48 516 98 510 168C504 238 524 306 510 386C496 466 524 548 510 628C496 708 526 782 452 816C382 848 300 826 230 836C152 846 74 842 44 782C18 730 42 664 32 584C22 504 46 432 32 352C18 272 46 204 32 134C26 106 42 92 62 78Z";

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, p.Primary)
                + Path(Blob, "#FFFFFF")
                + Circle(30, 26, 20, p.Secondary) + Circle(76, 12, 9, "#FFFFFF", "fill-opacity=\".75\"") + Circle(518, 52, 13, p.Secondary)
                + Circle(512, 838, 22, p.Secondary) + Circle(28, 828, 11, "#FFFFFF", "fill-opacity=\".75\"") + Circle(470, 14, 7, "#FFFFFF", "fill-opacity=\".75\"")
                + Circle(448, 190, 20, p.SecondaryTint) + Circle(92, 640, 18, p.PrimaryTint) + Circle(450, 600, 12, p.SecondaryTint);
            var ring = Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"12\"", 0.6f) + Photo.Shape($"fill=\"none\" stroke=\"{p.Primary}\" stroke-width=\"4\"", 1.9f);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(22, 3.8f, 10, 10), x, p.PrimaryTint);   // 10 mm (was 9); the plate has an edge now, so the pale tint cannot vanish on the white body
                Fit(l.At(6, HasLogo(x) ? 13.9f : 5, 42, HasLogo(x) ? 4f : 10), x.School.Name, 8.4f, p.PrimaryText, bold: true);
                l.At(9, 45.9f, 36, 6).Background(p.Secondary).CornerRadius(100).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text($"ID  {x.Card.AdmissionNumber}").FontFamily(Sans).FontSize(10).Bold().FontColor(p.OnSecondary).AlignCenter();
                Fit(l.At(5, 53f, 44, 6.2f), x.Card.FullName, 13, p.PrimaryText, bold: true);
                Fit(l.At(5, 59.1f, 44, 4), x.Card.ClassSection, 8.2f, p.Ink, semi: true);
                Rows(l.At(7, 64.4f, 40, 14.4f), x.Rows("roll", "father", "contact", "blood"), 11, 6.4f, p.Muted, p.Ink);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, p.Primary) + Path(Blob, "#FFFFFF")
                + Circle(30, 26, 20, p.Secondary) + Circle(518, 52, 13, p.Secondary) + Circle(512, 838, 22, p.Secondary) + Circle(28, 828, 11, "#FFFFFF", "fill-opacity=\".75\"")
                + Circle(450, 560, 16, p.SecondaryTint) + Circle(90, 700, 14, p.PrimaryTint);
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                if (HasLogo(x)) Logo(l.At(22, 4, 10, 10), x, p.PrimaryTint);
                Fit(l.At(6, HasLogo(x) ? 14.3f : 6, 42, HasLogo(x) ? 5.2f : 10), x.School.Name, 9, p.PrimaryText, bold: true);
                l.At(14, 22.2f, 26, 0.5f).Background(p.Secondary);
                Fit(l.At(6, 23.2f, 42, 3.2f), "For more information, scan this", 5.8f, p.Ink, semi: true);
                Qr(l, x, 27, 27.6f, 21f, p.Muted, p.PrimaryTint);
                QrCaption(l.At(5, 51.4f, 44, 2.4f), x, p.Muted, 4.6f);
                Fit(l.At(6, 56f, 42, 3.4f), "SCHOOL ADDRESS", 6.6f, p.Ink, bold: true, spacing: 0.16f);
                Contact(l.At(7, 59.8f, 40, 12.4f), x, 6.2f, p.Muted, p.Ink);
                if (x.TermsLines().Count > 0) Terms(l.At(7, 72.4f, 40, 8f), x, 5f, p.Muted, "•");
            });
        }
    }

    // =================================================================================================================================
    // 5. modern-navy: dark base with accent curves, big round photo, name + father prominent, class badge; back with terms, signature, QR
    // =================================================================================================================================
    internal sealed class ModernNavy : IdCardTemplate
    {
        public override string Code => "modern-navy";
        public override string Name => "Modern Navy";
        public override string Description => "Dark curves, a big round photo and a grade badge.";
        public override string DefaultPrimary => "#14213D";
        public override string DefaultSecondary => "#FF7A18";
        private static readonly Spot Photo = Spot.Circle(30.5f, 33f, 12.2f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var onP = p.OnPrimary;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V318C500 452 392 506 262 506C130 506 36 450 0 338Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 338C36 450 130 506 262 506C392 506 500 452 540 318V332C500 470 392 522 262 522C124 522 30 462 0 352Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Path("M540 0V230C460 214 372 130 392 0Z", IdCardPalette.Mix(p.Primary, onP, 0.12))
                + Path("M0 856V812C150 792 330 836 540 800V856Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 812C150 792 330 836 540 800V810C330 846 150 802 0 822Z", p.Secondary);
            var ring = Photo.Shape($"fill=\"none\" stroke=\"{p.Primary}\" stroke-width=\"16\"", 0.8f) + Photo.Shape("fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"5\"", 1.8f)
                + Rosette(78, 478, 52, p.Primary) + Circle(78, 478, 41, "none", $"stroke=\"{onP}\" stroke-width=\"3\" stroke-dasharray=\"3 4\"")
                + Rect(50, 566, 8, 226, p.Secondary, "rx=\"4\"");
            var ink = p.Ink;

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(4, 3, 8, 8), x);
                Fit(l.At(HasLogo(x) ? 13.5f : 4, 2.8f, HasLogo(x) ? 36.5f : 46, 8.4f), x.School.Name, 8.6f, onP, HasLogo(x) ? Align.Left : Align.Center, bold: true);
                // grade badge (rosette) on the left
                Fit(l.At(2.8f, 44.6f, 10.4f, 2.6f), "GRADE", 3.9f, onP, semi: true, spacing: 0.1f);
                Fit(l.At(2.8f, 46.8f, 10.4f, 5.4f), IdCardKit_Grade(x), 11f, onP, bold: true);
                // labelled details with the accent bar
                var rows = new List<(string L, string V)>();
                rows.Add(("Name:", x.Card.FullName));
                var father = x.Rows("father").FirstOrDefault();
                if (!string.IsNullOrEmpty(father.Value)) rows.Add((father.Label == "Father" ? "Father Name:" : father.Label + ":", father.Value));
                rows.Add(("ID Number:", x.Card.AdmissionNumber));
                var extra = string.Join("   ", new[] { x.Card.ClassSection, string.IsNullOrWhiteSpace(x.Card.RollNumber) ? null : "Roll " + x.Card.RollNumber }.Where(v => v != null));
                rows.Add(("Class:", extra));
                var dob = x.Rows("dob", "blood");
                if (dob.Count > 0) rows.Add(("DOB / Blood:", string.Join("   ", dob.Select(r => r.Value))));
                l.At(6.6f, 56.4f, 44, 23.6f).AlignTop().ScaleToFit().Column(col =>
                {
                    col.Spacing(0.5f, Mm);
                    foreach (var (lab, val) in rows)
                        col.Item().Column(cc =>
                        {
                            cc.Item().Text(lab).FontFamily(Sans).FontSize(5.2f).FontColor(p.Muted).LineHeight(1f);
                            cc.Item().Text(val).FontFamily(Sans).FontSize(lab == "Name:" ? 9.8f : 7.2f).SemiBold().FontColor(ink).LineHeight(1.05f);
                        });
                });
                Fit(l.At(0, 82.1f, 54, 3.2f), $"Session {x.Card.YearName}", 5.8f, p.OnPrimary, semi: true);
            });
        }

        private static string IdCardKit_Grade(CardCtx x) => GradeLabel(x.Card.ClassName);

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var soft = IdCardPalette.Mix(p.Primary, p.OnPrimary, 0.12);
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H356C336 160 214 262 0 282Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M356 0C336 160 214 262 0 282V296C230 274 360 172 382 0Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Path("M0 0H190C170 90 100 150 0 170Z", soft)
                + Path("M0 856V790C0 716 118 664 270 664C422 664 540 716 540 790V856Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark));
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                Fit(l.At(4, 3.6f, 30, 2.8f), "This card is valid for", 5.2f, p.OnPrimary, Align.Left);
                Fit(l.At(4, 6.4f, 30, 4.6f), $"Session {x.Card.YearName}", 8f, p.OnPrimary, Align.Left, bold: true);
                if (HasLogo(x)) Logo(l.At(41.5f, 3.4f, 8.6f, 8.6f), x);
                Fit(l.At(6, 30.4f, 42, 5), "Terms & Conditions", 9.4f, p.PrimaryText, bold: true);
                Terms(l.At(7.5f, 36.2f, 39, 13), x, 5.6f, p.Ink, "•");
                Signature(l, x, 5.5f, 50.4f, 21, 10.8f, p.Ink, p.Muted);
                Qr(l, x, 40f, 49.6f, 11.4f, p.Muted);
                Fit(l.At(30, 63.2f, 21, 2f), x.Card.QrPayload.Replace(IdCardQr.Separator, " / "), 3.6f, p.Muted);
                Rows(l.At(6.5f, 62.2f, 23, 6.6f), x.SchoolContact().Where(r => r.Label == "Phone" || (r.Label == "Email" && r.Value.Length <= 22)).ToList(), 6.2f, 5.2f, p.Muted, p.Ink);
                Fit(l.At(6, 72.4f, 42, 3.2f), x.School.Address ?? "", 4.9f, IdCardPalette.On(p.Secondary), semi: true);
                Fit(l.At(6, 75.6f, 42, 5.6f), x.School.Name, 8.6f, IdCardPalette.On(p.Secondary), bold: true);
                if (!string.IsNullOrWhiteSpace(x.Brand.Tagline)) Fit(l.At(6, 81.2f, 42, 3), x.Brand.Tagline!, 5.2f, IdCardPalette.On(p.Secondary), semi: true);
            });
        }
    }

    // =================================================================================================================================
    // 6. formal-classic: traditional header in a serif face, rectangular photo frame, formal rows, principal signature at the bottom
    // =================================================================================================================================
    internal sealed class FormalClassic : IdCardTemplate
    {
        public override string Code => "formal-classic";
        public override string Name => "Formal Classic";
        public override string Description => "Traditional serif header, framed photo, session strip and a signature.";
        public override string DefaultPrimary => "#1B2A5C";
        public override string DefaultSecondary => "#B08D2A";
        private static readonly Spot Photo = Spot.Box(8.6f, 25.2f, 19.4f, 24.2f, 2.4f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Rect(0, 0, 540, 214, p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Rect(0, 218, 540, 5, p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Rect(0, 223, 62, 633, p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Rect(62, 223, 5, 633, p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Rect(67, 826, 473, 30, p.Primary) + Rect(67, 820, 473, 4, p.Secondary);
            var frame = Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"7\"", 0.3f) + Photo.Shape($"fill=\"none\" stroke=\"{p.Primary}\" stroke-width=\"2.5\"", 1.2f);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, frame));
                InitialsOver(l, x, Photo);
                // header: name in serif, then address and phone / tagline
                var line1 = x.School.Address;
                var line2 = string.Join("  ·  ", new[] { string.IsNullOrWhiteSpace(x.School.Phone) ? null : "Ph: " + x.School.Phone, x.Brand.Tagline }.Where(v => !string.IsNullOrWhiteSpace(v)));
                var muted = IdCardPalette.Mix(p.Primary, p.OnPrimary, 0.82);
                Fit(l.At(3, 1.4f, 48, (line1 != null || line2.Length > 0) ? 11.2f : 16), x.School.Name, 10.2f, p.OnPrimary, bold: true, serif: true);
                if (!string.IsNullOrWhiteSpace(line1)) Fit(l.At(3, 12.6f, 48, 4.6f), line1!, 4.9f, muted, serif: true);
                if (line2.Length > 0) Fit(l.At(3, 17.2f, 48, 3.2f), line2, 4.9f, muted, serif: true);
                // session strip (vertical)
                l.At(0, 22.6f, 6.2f, 62.6f).RotateLayoutCounterclockwise().AlignMiddle().AlignCenter().ScaleToFit()
                    .Text($"SESSION  {x.Card.YearName}").FontFamily(new[] { Serif, Sans }).FontSize(6.8f).Bold().FontColor(p.OnPrimary).LetterSpacing(0.14f);

                Fit(l.At(30, 24.2f, 21, 3.4f), "IDENTITY CARD", 6.4f, p.PrimaryText, Align.Left, bold: true, serif: true, spacing: 0.08f);
                l.At(30, 27.8f, 9, 0.5f).Background(p.Secondary);
                if (HasLogo(x)) Logo(l.At(32, 28.9f, 17, 17), x, "#FFFFFF");   // 17 mm (was 15): centred in the column right of the photo, clear of the rule above and the admission number below
                Fit(l.At(30, HasLogo(x) ? 46.4f : 31f, 21, 2.8f), "Admission No.", 5f, p.Muted, Align.Left, serif: true);
                Fit(l.At(30, HasLogo(x) ? 49f : 33.8f, 21, 4.2f), x.Card.AdmissionNumber, 7.8f, p.PrimaryText, Align.Left, bold: true, serif: true);

                Rows(l.At(8.6f, 53.2f, 42.4f, 19.6f), x.Rows("roll", "name", "class", "dob", "contact", "address"), 13.2f, 6.2f, p.Ink, p.Ink, serif: true);
                Signature(l, x, 31, 73.4f, 19, 9f, p.Ink, p.Muted, 5.2f, serif: true);
                Fit(l.At(8.6f, 82.4f, 40, 2.6f), "STUDENT IDENTITY CARD", 4.4f, p.OnPrimary, Align.Left, semi: true, spacing: 0.12f);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Rect(0, 0, 540, 120, p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Rect(0, 124, 540, 5, p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark)) + Rect(0, 135, 540, 2, p.Secondary)
                + Rect(0, 826, 540, 30, p.Primary) + Rect(0, 820, 540, 4, p.Secondary);
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                if (HasLogo(x)) Logo(l.At(3.2f, 1.6f, 9, 9), x);
                Fit(l.At(HasLogo(x) ? 14 : 3, 1.2f, HasLogo(x) ? 37.5f : 48, 9.6f), x.School.Name, 9.4f, p.OnPrimary, bold: true, serif: true);
                Fit(l.At(5, 15.6f, 44, 3.6f), "IF FOUND, PLEASE RETURN TO", 6, p.PrimaryText, Align.Left, bold: true, serif: true, spacing: 0.08f);
                Contact(l.At(5, 19.6f, 44, 15), x, 6.2f, p.Muted, p.Ink, 9.5f, serif: true);
                Fit(l.At(5, 36.6f, 44, 3.6f), "TERMS & CONDITIONS", 6, p.PrimaryText, Align.Left, bold: true, serif: true, spacing: 0.08f);
                Terms(l.At(5, 40.4f, 44, 14), x, 5.6f, p.Ink, "•", serif: true);
                Qr(l, x, 27, 58.3f, 17f, p.Muted);
                QrCaption(l.At(5, 76.4f, 44, 2.4f), x, p.Muted);
                Fit(l.At(0, 83.2f, 54, 2.4f), $"VALID FOR SESSION {x.Card.YearName}", 4.6f, p.OnPrimary, semi: true, spacing: 0.12f);
            });
        }
    }

}
