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
        private static readonly Spot Photo = Spot.Circle(27, 32, 10.5f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V236C420 300 120 300 0 236Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 236C120 300 420 300 540 236V252C420 316 120 316 0 252Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Circle(505, 30, 95, "#FFFFFF", "fill-opacity=\".09\"") + Circle(20, 170, 70, "#FFFFFF", "fill-opacity=\".08\"")
                + Rect(0, 808, 540, 48, p.Primary) + Rect(0, 804, 540, 5, p.Secondary);
            var ring = Photo.Shape("fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"13\"", 0.6f) + Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"4\"", 1.5f);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(22.5f, 1.8f, 9, 9), x);
                Fit(l.At(4, HasLogo(x) ? 11 : 4.5f, 46, HasLogo(x) ? 8 : 12), x.School.Name, 9.5f, p.OnPrimary, bold: true);
                Fit(l.At(3, 44.5f, 48, 6), x.Card.FullName, 13, p.PrimaryText, bold: true);
                l.At(14, 51.4f, 26, 4.8f).Background(p.Secondary).CornerRadius(100).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text(x.Card.ClassSection).FontFamily(Sans).FontSize(7.8f).SemiBold().FontColor(p.OnSecondary).AlignCenter();
                Rows(l.At(5.5f, 58.2f, 43, 22), x.Rows("adm", "roll", "father", "contact", "dob", "blood"), 14, 6.8f, p.Muted, p.Ink);
                Fit(l.At(0, 81.2f, 54, 4.4f), $"Session {x.Card.YearName}", 6.4f, p.OnPrimary, semi: true);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Path("M0 0H540V126C400 166 140 166 0 126Z", p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Path("M0 126C140 166 400 166 540 126V140C400 180 140 180 0 140Z", p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark))
                + Circle(500, 20, 80, "#FFFFFF", "fill-opacity=\".08\"")
                + Rect(0, 818, 540, 38, p.Primary) + Rect(0, 814, 540, 5, p.Secondary);
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                if (HasLogo(x)) Logo(l.At(23, 1.5f, 8, 8), x);
                Fit(l.At(4, HasLogo(x) ? 9.4f : 3.5f, 46, HasLogo(x) ? 4.6f : 9), x.School.Name, 8.6f, p.OnPrimary, bold: true);
                if (!string.IsNullOrWhiteSpace(x.Brand.Tagline)) Fit(l.At(6, 17.4f, 42, 3.2f), x.Brand.Tagline!, 5.6f, p.Muted, semi: true);

                Fit(l.At(5, 21.4f, 44, 3.6f), "SCHOOL CONTACT", 6.2f, p.PrimaryText, Align.Left, bold: true, spacing: 0.08f);
                l.At(5, 24.8f, 12, 0.5f).Background(p.Secondary);
                Contact(l.At(5, 26.2f, 44, 14), x, 6.2f, p.Muted, p.Ink);

                Fit(l.At(5, 42, 44, 3.6f), "TERMS & CONDITIONS", 6.2f, p.PrimaryText, Align.Left, bold: true, spacing: 0.08f);
                l.At(5, 45.4f, 12, 0.5f).Background(p.Secondary);
                Terms(l.At(5, 46.8f, 44, 11.6f), x, 5.6f, p.Ink, "•");

                Qr(l, x, 27, 60.4f, 16.4f, p.Muted);
                QrCaption(l.At(5, 78.6f, 44, 2.2f), x, p.Muted);
                Fit(l.At(0, 81.8f, 54, 3.6f), $"Valid for session {x.Card.YearName}", 5.8f, p.OnPrimary, semi: true);
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
                Fit(l.At(HasLogo(x) ? 15 : 4, 2.5f, HasLogo(x) ? 35 : 46, 10.5f), x.School.Name, 9, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, bold: true);
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
                Fit(l.At(4, HasLogo(x) ? 16.5f : 8, 46, HasLogo(x) ? 9 : 13), x.School.Name, 10.5f, p.OnPrimary, bold: true);
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
                Fit(l.At(HasLogo(x) ? 13.5f : 4, 2.2f, HasLogo(x) ? 37 : 46, 9.5f), x.School.Name, 9.4f, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, bold: true);
                l.At(15, 12.2f, 24, 4.2f).Background(p.Secondary).CornerRadius(100).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text($"SESSION {x.Card.YearName}").FontFamily(Sans).FontSize(6.2f).Bold().FontColor(p.OnSecondary).LetterSpacing(0.05f).AlignCenter();
                Fit(l.At(3, 43.3f, 48, 5.4f), x.Card.FullName, 12, p.PrimaryText, bold: true);
                Fit(l.At(3, 48.6f, 48, 3.8f), x.Card.ClassSection, 7.6f, p.Ink, semi: true);
                Rows(l.At(5, 53.6f, 44, 27), x.Rows("adm", "roll", "dob", "blood", "father", "mother", "contact", "address"), 12, 6.3f, p.Muted, p.Ink);
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
                if (HasLogo(x)) Logo(l.At(22.5f, 3.6f, 9, 9), x, p.PrimaryTint);
                Fit(l.At(6, HasLogo(x) ? 12.6f : 5, 42, HasLogo(x) ? 5.2f : 10), x.School.Name, 8.4f, p.PrimaryText, bold: true);
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
                if (HasLogo(x)) Logo(l.At(22.5f, 4, 9, 9), x, p.PrimaryTint);
                Fit(l.At(6, HasLogo(x) ? 13.2f : 6, 42, HasLogo(x) ? 5.6f : 10), x.School.Name, 9, p.PrimaryText, bold: true);
                if (!string.IsNullOrWhiteSpace(x.Brand.Tagline)) Fit(l.At(7, 19, 40, 3.6f), x.Brand.Tagline!, 6.2f, p.Muted, semi: true);
                Qr(l, x, 27, 24.5f, 22f, p.Muted, p.PrimaryTint);
                QrCaption(l.At(5, 49.4f, 44, 2.6f), x, p.Muted, 4.8f);
                Contact(l.At(7, 54, 40, 14), x, 6.2f, p.Muted, p.Ink);
                if (x.TermsLines().Count > 0) Terms(l.At(7, 69, 40, 9.5f), x, 5.2f, p.Muted, "•");
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
        public override string Description => "Dark base with accent curves, a big round photo and a grade badge.";
        public override string DefaultPrimary => "#14213D";
        public override string DefaultSecondary => "#FF7A18";
        private static readonly Spot Photo = Spot.Circle(27, 29, 12);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var soft = IdCardPalette.Mix(p.Primary, p.OnPrimary, 0.12);
            var art = Rect(0, 0, 540, 856, p.Primary)
                + Path("M540 0V230C430 215 340 120 372 0Z", soft)
                + Path("M0 690C140 640 330 720 540 660V856H0Z", soft)
                + Path("M0 624C140 600 330 650 540 612V622C330 660 140 610 0 634Z", p.Secondary)
                + Path("M0 856V812C200 782 380 846 540 798V856Z", p.Secondary);
            var ring = Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"11\"", 0.5f) + Photo.Shape($"fill=\"none\" stroke=\"{p.OnPrimary}\" stroke-opacity=\".25\" stroke-width=\"3\"", 1.9f);
            var on = p.OnPrimary;
            var dim = IdCardPalette.Mix(p.Primary, on, 0.72);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, ring));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(4, 3, 8, 8), x);
                Fit(l.At(HasLogo(x) ? 13.5f : 4, 2.8f, HasLogo(x) ? 36.5f : 46, 8.4f), x.School.Name, 8.4f, on, HasLogo(x) ? Align.Left : Align.Center, bold: true);
                Fit(l.At(3, 42.8f, 48, 6.6f), x.Card.FullName, 13.5f, on, bold: true);
                var father = x.Rows("father").FirstOrDefault();
                if (!string.IsNullOrEmpty(father.Value)) Fit(l.At(3, 49.4f, 48, 4), $"{(father.Label == "Father" ? "S/o" : father.Label)}  {father.Value}", 7.4f, dim, semi: true);
                l.At(14, 54.4f, 26, 6).Background(p.Secondary).CornerRadius(1.6f).AlignMiddle().AlignCenter().ScaleToFit()
                    .Text(x.Card.ClassSection.ToUpperInvariant()).FontFamily(Sans).FontSize(8.6f).Bold().FontColor(p.OnSecondary).LetterSpacing(0.04f).AlignCenter();
                Rows(l.At(6, 65f, 42, 15f), x.Rows("adm", "roll", "contact", "dob", "blood"), 13, 6.4f, dim, on);
                Fit(l.At(0, 81.4f, 54, 4), $"Session {x.Card.YearName}", 6.2f, p.OnSecondary, semi: true);
            });
        }

        public override void Back(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var soft = IdCardPalette.Mix(p.Primary, p.OnPrimary, 0.12);
            var art = Rect(0, 0, 540, 856, p.Primary)
                + Path("M540 0V180C440 170 350 100 372 0Z", soft)
                + Rect(30, 150, 480, 650, "#FFFFFF", "rx=\"34\"")
                + Path("M0 856V812C200 782 380 846 540 798V856Z", p.Secondary)
                + Rect(30, 150, 480, 14, p.Secondary, "rx=\"7\"");
            c.Layers(l =>
            {
                l.PrimaryLayer().Svg(Plain(art));
                if (HasLogo(x)) Logo(l.At(4, 4, 9, 9), x);
                Fit(l.At(HasLogo(x) ? 14.5f : 4, 3.6f, HasLogo(x) ? 35.5f : 46, 10), x.School.Name, 9.4f, p.OnPrimary, HasLogo(x) ? Align.Left : Align.Center, bold: true);
                Fit(l.At(6, 18.5f, 42, 3.6f), "TERMS & CONDITIONS", 6.4f, p.Ink, Align.Left, bold: true, spacing: 0.08f);
                Terms(l.At(6, 22.4f, 42, 14), x, 5.6f, p.Ink, "•");
                Fit(l.At(6, 38.2f, 42, 3.6f), "SCHOOL CONTACT", 6.4f, p.Ink, Align.Left, bold: true, spacing: 0.08f);
                Contact(l.At(6, 42, 42, 12.5f), x, 6, p.Muted, p.Ink);
                Qr(l, x, 38.5f, 58f, 13f, p.Muted, "#FFFFFF");
                QrCaption(l.At(28, 72.6f, 21, 2.2f), x, p.Muted, 3.9f);
                Signature(l, x, 6, 57.5f, 19, 14, p.Ink, p.Muted);
                Fit(l.At(0, 81.8f, 54, 3.6f), $"Valid for session {x.Card.YearName}", 5.8f, p.OnSecondary, semi: true);
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
        public override string Description => "Traditional serif header, framed photo and formal rows with a signature.";
        public override string DefaultPrimary => "#1B2A5C";
        public override string DefaultSecondary => "#B08D2A";
        private static readonly Spot Photo = Spot.Box(4.5f, 25.6f, 19.5f, 24.5f, 0.6f);

        public override void Front(IContainer c, CardCtx x)
        {
            var p = x.Pal;
            var art = Rect(0, 0, 540, 856, "#FFFFFF")
                + Rect(0, 0, 540, 210, p.Primary, PaleEdge(p.PrimaryIsPale, p.PrimaryDark))
                + Rect(0, 214, 540, 5, p.Secondary, PaleEdge(p.SecondaryIsPale, p.PrimaryDark)) + Rect(0, 225, 540, 2, p.Secondary)
                + Rect(0, 826, 540, 30, p.Primary) + Rect(0, 820, 540, 4, p.Secondary);
            var frame = Photo.Shape($"fill=\"none\" stroke=\"{p.Secondary}\" stroke-width=\"7\"", 0.3f) + Photo.Shape($"fill=\"none\" stroke=\"{p.Primary}\" stroke-width=\"2\"", 1.1f);

            c.Layers(l =>
            {
                Backdrop(l, x, Photo);
                l.PrimaryLayer().Svg(Art(Photo, art, frame));
                InitialsOver(l, x, Photo);
                if (HasLogo(x)) Logo(l.At(3.2f, 4.4f, 11.5f, 11.5f), x);
                var hasTag = !string.IsNullOrWhiteSpace(x.Brand.Tagline);
                Fit(l.At(HasLogo(x) ? 15.5f : 3, 2.6f, HasLogo(x) ? 36 : 48, hasTag ? 13 : 16.2f), x.School.Name, 10.4f, p.OnPrimary, bold: true, serif: true);
                if (hasTag) Fit(l.At(HasLogo(x) ? 15.5f : 3, 15.4f, HasLogo(x) ? 36 : 48, 3.6f), x.Brand.Tagline!, 5.6f, IdCardPalette.Mix(p.Primary, p.OnPrimary, 0.8), serif: true);

                Fit(l.At(26, 26.2f, 24, 4), "IDENTITY CARD", 7.4f, p.PrimaryText, Align.Left, bold: true, serif: true, spacing: 0.1f);
                l.At(26, 30.4f, 10, 0.5f).Background(p.Secondary);
                Fit(l.At(26, 32f, 24, 3.6f), $"Session {x.Card.YearName}", 6.6f, p.Muted, Align.Left, serif: true);
                Fit(l.At(26, 36.4f, 24, 3.2f), "Admission No.", 5.6f, p.Muted, Align.Left, serif: true);
                Fit(l.At(26, 39.4f, 24, 5), x.Card.AdmissionNumber, 9.4f, p.PrimaryText, Align.Left, bold: true, serif: true);

                Rows(l.At(4.5f, 52.2f, 45, 20.4f), x.Rows("roll", "name", "class", "dob", "contact", "address"), 13.5f, 6.4f, p.Muted, p.Ink, serif: true);
                Signature(l, x, 31, 73f, 19, 9.4f, p.Ink, p.Muted, 5.4f, serif: true);
                Fit(l.At(4.5f, 75.6f, 24, 3f), "Valid for the session", 5, p.Muted, Align.Left, serif: true);
                Fit(l.At(4.5f, 78.4f, 24, 3.6f), x.Card.YearName, 6.6f, p.PrimaryText, Align.Left, bold: true, serif: true);
                Fit(l.At(0, 83.2f, 54, 2.4f), "STUDENT IDENTITY CARD", 4.6f, p.OnPrimary, semi: true, spacing: 0.12f);
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
                Fit(l.At(0, 83.2f, 54, 2.4f), $"VALID FOR SESSION {x.Card.YearName}", 4.6f, p.OnPrimary, semi: true, spacing: 0.1f);
            });
        }
    }
}
